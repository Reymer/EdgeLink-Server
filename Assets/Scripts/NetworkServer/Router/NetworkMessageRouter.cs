using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using iotserver;

/// <summary>
/// 網路訊息路由器
/// </summary>
public class NetworkMessageRouter
{
    private static NetworkMessageRouter instance;
    public static NetworkMessageRouter Instance => instance ??= new NetworkMessageRouter();

    private readonly ConcurrentDictionary<string, TCPClientData> tcpClients = new();
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServers = new();

    private const int ResponseTimeoutMs = 5000;

    private NetworkMessageRouter() { }

    // ── TCP Client 註冊 ──────────────────────────────────────────────

    public void RegisterTcpClient(string protocolKey, TCPClientData clientData) =>
        tcpClients[protocolKey] = clientData;

    public void UnregisterTcpClient(string protocolKey) =>
        tcpClients.TryRemove(protocolKey, out _);

    // ── TCP Server 註冊（供反向路由查詢）────────────────────────────

    public void RegisterTcpServer(string portId, TCPServerData serverData) =>
        tcpServers[portId] = serverData;

    public void UnregisterTcpServer(string portId) =>
        tcpServers.TryRemove(portId, out _);

    // ── 正向路由：TCP Server 收到 → 轉給 TCP Client ─────────────────

    /// <summary>
    /// 路由 TCP Server 收到的封包（必須在 ThreadPool 上 await）
    /// </summary>
    public UniTask RouteMessageAsync(TCPServerData serverData, byte[] rawBytes, string parsedMessage,
        System.Net.IPEndPoint sourceEndpoint, string clientKey)
    {
        RouterLogHelper.LogReceive(serverData.portData, MonitorTargetType.TCPServer, parsedMessage, sourceEndpoint);
        return RouteAndForwardAsync(serverData.portData, rawBytes, parsedMessage, clientKey, serverData);
    }

    private async UniTask RouteAndForwardAsync(PortData portData, byte[] rawBytes, string parsedMessage,
        string clientKey, TCPServerData serverData)
    {
        try
        {
            var targets = GetTargetClients(portData.Id, portData.ProtocolName);
            if (targets.Count == 0) return;

            if (targets.Count == 1)
                await ProcessAndForward(targets[0], portData.ProtocolName, rawBytes, parsedMessage, clientKey, serverData);
            else
                await UniTask.WhenAll(targets.Select(t =>
                    ProcessAndForward(t, portData.ProtocolName, rawBytes, parsedMessage, clientKey, serverData)));
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_UnexpectedError)}: {ex}", isError: true);
        }
    }

    private async UniTask ProcessAndForward(TCPClientData client, string protocolName,
        byte[] rawBytes, string parsedMessage, string clientKey, TCPServerData serverData)
    {
        string maskId = client.portData?.MaskType?.Trim() ?? "OriginalData";
        var def = MaskDefinitionManager.Instance.GetDefinition(maskId)
               ?? MaskDefinitionManager.Instance.GetDefinition("OriginalData");

        if (def == null)
        {
            LogHelper.LogToConsole($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_MaskNotFound)}: '{maskId}'", isError: true);
            return;
        }

        bool isConcurrent = string.Equals(client.portData?.RequestMode, "concurrent", StringComparison.OrdinalIgnoreCase);
        bool isPolling    = string.Equals(client.portData?.RequestMode, "polling",    StringComparison.OrdinalIgnoreCase);

        if (isPolling)
        {
            string output = MaskProcessor.Process(def, rawBytes, parsedMessage);
            if (output == null) return;

            var bytes = Encoding.UTF8.GetBytes(output.EndsWith("\n") ? output : output + "\n");
            var stream = serverData?.ClientStreams.GetValueOrDefault(clientKey);
            client.LatestPollRequest = new PollSlot
            {
                Requester = new PendingRequest { ClientKey = clientKey, Stream = stream, EnqueueTime = DateTime.UtcNow },
                Data = bytes
            };
            try { client.PollTrigger.Release(); } catch (System.Threading.SemaphoreFullException) { }
        }
        else if (isConcurrent)
        {
            string correlationId = Guid.NewGuid().ToString("N")[..8];
            var extra = new Dictionary<string, string> { ["_corrId"] = correlationId };
            string output = MaskProcessor.Process(def, rawBytes, parsedMessage, extra);
            if (output == null) return;

            var bytes = Encoding.UTF8.GetBytes(output.EndsWith("\n") ? output : output + "\n");

            var stream = serverData?.ClientStreams.GetValueOrDefault(clientKey);
            client.PendingRequests[correlationId] = new PendingRequest
            {
                ClientKey = clientKey,
                Stream = stream,
                EnqueueTime = DateTime.UtcNow
            };

            CleanupStalePendingRequests(client);
            await TrySendToClient(client, protocolName, bytes);
        }
        else
        {
            // Serial 模式：處理後放入 queue，由 TCPClientConnector 的 queue processor 依序送出
            string output = MaskProcessor.Process(def, rawBytes, parsedMessage);
            if (output == null) return;

            var bytes = Encoding.UTF8.GetBytes(output.EndsWith("\n") ? output : output + "\n");
            var stream = serverData?.ClientStreams.GetValueOrDefault(clientKey);
            var requester = new PendingRequest
            {
                ClientKey = clientKey,
                Stream = stream,
                EnqueueTime = DateTime.UtcNow
            };

            client.RequestQueue.Enqueue((requester, bytes));
        }
    }

    // ── 反向路由：TCP Client 收到設備回應 → 路由回 Frontend ─────────

    public async UniTask RouteResponseAsync(TCPClientData clientData, byte[] rawBytes, string text)
    {
        var portData = clientData.portData;
        RouterLogHelper.LogReceive(portData, MonitorTargetType.TCPClient, text, null);

        if (!tcpServers.TryGetValue(portData.SourceProtocolId ?? "", out var serverData))
            return;

        string maskId = portData.ResponseMaskType?.Trim() ?? "OriginalData";
        var def = MaskDefinitionManager.Instance.GetDefinition(maskId)
               ?? MaskDefinitionManager.Instance.GetDefinition("OriginalData");

        if (def == null)
        {
            LogHelper.LogToConsole($"[Router] Response mask not found: '{maskId}'", isError: true);
            return;
        }

        string output = MaskProcessor.Process(def, rawBytes, text);
        if (output == null) return;

        var bytes = Encoding.UTF8.GetBytes(output.EndsWith("\n") ? output : output + "\n");
        string routeMode = def.routeMode?.Trim().ToLower() ?? "broadcast";

        if (routeMode == "response")
        {
            bool isConcurrent = string.Equals(portData.RequestMode, "concurrent", StringComparison.OrdinalIgnoreCase);

            if (isConcurrent)
            {
                // Concurrent：從回應中提取 correlationId，找對應的 requester
                if (string.IsNullOrEmpty(def.correlationIdField)) return;

                var fields = ExtractFields(def, text);
                if (!fields.TryGetValue(def.correlationIdField, out var corrId)) return;

                if (!clientData.PendingRequests.TryRemove(corrId, out var requester)) return;

                await TrySendToServerClient(serverData, requester.ClientKey, requester.Stream, bytes, portData.ProtocolName);
            }
            else
            {
                // Serial：回給 CurrentPendingRequester，並通知 queue processor 繼續
                var requester = clientData.CurrentPendingRequester;
                if (requester == null) return;

                await TrySendToServerClient(serverData, requester.ClientKey, requester.Stream, bytes, portData.ProtocolName);

                clientData.CurrentPendingRequester = null;
                clientData.ResponseSignal?.TrySetResult(true);
            }
        }
        else
        {
            // Broadcast：送給所有連入 TCP Server 的 frontend clients
            await BroadcastToServerClients(serverData, bytes, portData.ProtocolName);
        }
    }

    // ── 寫回 TCP Server 的 client stream ────────────────────────────

    private async UniTask TrySendToServerClient(TCPServerData serverData, string clientKey,
        System.Net.Sockets.NetworkStream stream, byte[] data, string protocolName)
    {
        if (stream == null) return;

        SemaphoreSlim writeLock = null;
        serverData?.ClientWriteLocks.TryGetValue(clientKey ?? "", out writeLock);
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(2000);
            if (writeLock != null) await writeLock.WaitAsync(cts.Token);
            try
            {
                await stream.WriteAsync(data, 0, data.Length, cts.Token);
                RouterLogHelper.LogSend(serverData.portData, MonitorTargetType.TCPServer,
                    Encoding.UTF8.GetString(data));
            }
            finally
            {
                writeLock?.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (System.IO.IOException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] 回寫 frontend client 失敗 [{protocolName}]: {ex.Message}", isError: true);
        }
    }

    private async UniTask BroadcastToServerClients(TCPServerData serverData, byte[] data, string protocolName)
    {
        var clients = serverData.ClientStreams.ToList();
        if (clients.Count == 0) return;

        var tasks = clients.Select(kv =>
            TrySendToServerClient(serverData, kv.Key, kv.Value, data, protocolName));
        await UniTask.WhenAll(tasks);
    }


    // ── Helpers ──────────────────────────────────────────────────────

    public List<TCPClientData> GetTargetClients(string sourceId, string sourceName)
    {
        var targets = new List<TCPClientData>();
        foreach (var c in tcpClients.Values.ToList())
        {
            if (c?.portData == null) continue;
            if (c.portData.SourceProtocolId == sourceId) targets.Add(c);
        }
        return targets;
    }

    private async UniTask TrySendToClient(TCPClientData tcpClient, string protocolName, byte[] data)
    {
        if (tcpClient?.portData == null) return;
        if (!tcpClient.portData.IsConnected) return;

        try
        {
            var client = tcpClient.tcpClient;
            if (client == null || !client.Connected) return;

            var stream = client.GetStream();
            if (stream == null || !stream.CanWrite)
            {
                tcpClient.portData.IsConnected = false;
                UniTask.Post(() =>
                    SafeExecution.Safe(() => tcpClient.portData.OnUpdate?.Invoke(tcpClient.portData)));
                return;
            }

            // 取得裝置寫入鎖，與 NotifyAsync 互斥
            using var cts = new System.Threading.CancellationTokenSource(2000);
            await tcpClient.DeviceWriteLock.WaitAsync(cts.Token);
            try
            {
                await stream.WriteAsync(data, 0, data.Length, cts.Token);
            }
            finally
            {
                try { tcpClient.DeviceWriteLock.Release(); } catch (ObjectDisposedException) { }
            }

            string parsedMessage = Encoding.UTF8.GetString(data);
            RouterLogHelper.LogSend(tcpClient.portData, MonitorTargetType.TCPClient, parsedMessage);
        }
        catch (OperationCanceledException) { }
        catch (System.IO.IOException) { }
        catch (System.ObjectDisposedException) { }
        catch (System.InvalidOperationException) { }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_ForwardFailed)} → {protocolName}: {ex.Message}", isError: true);

            try { tcpClient.tcpClient?.Close(); tcpClient.tcpClient?.Dispose(); }
            catch (Exception) { }

            tcpClient.tcpClient = null;
            tcpClient.portData.IsConnected = false;

            UniTask.Post(() =>
                SafeExecution.Safe(() => tcpClient.portData.OnUpdate?.Invoke(tcpClient.portData)));
        }
    }

    private static Dictionary<string, string> ExtractFields(MaskDefinition def, string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return result;

        var fieldDelim = string.IsNullOrEmpty(def.fieldDelimiter) ? ";" : def.fieldDelimiter;
        var kvSep = string.IsNullOrEmpty(def.kvSeparator) ? ":" : def.kvSeparator;

        foreach (var field in text.Split(new[] { fieldDelim }, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = field.IndexOf(kvSep, StringComparison.Ordinal);
            if (idx < 0) continue;
            var key = field.Substring(0, idx).Trim();
            var val = field.Substring(idx + kvSep.Length).Trim();
            if (!string.IsNullOrEmpty(key))
                result[key] = val;
        }

        return result;
    }

    private static void CleanupStalePendingRequests(TCPClientData client)
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-ResponseTimeoutMs);
        foreach (var kv in client.PendingRequests.ToList())
        {
            if (kv.Value.EnqueueTime < cutoff)
                client.PendingRequests.TryRemove(kv.Key, out _);
        }
    }

    public TCPClientData GetTcpClient(string protocolName)
    {
        tcpClients.TryGetValue(protocolName, out var clientData);
        return clientData;
    }
}
