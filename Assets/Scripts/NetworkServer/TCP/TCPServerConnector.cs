using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using DevKit;
using iotserver;
using UnityEngine;

/// <summary>
/// TCP Server 連接器
/// </summary>
public class TCPServerConnector : NetworkConnectorBase
{
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServers = new();
    private const int MAX_CONNECTIONS_PER_SERVER = 100;
    private const int MAX_BUFFER_SIZE = 1024 * 1024;
    private readonly IMainThreadDispatcher dispatcher;

    public TCPServerConnector(IMainThreadDispatcher dispatcher = null)
    {
        this.dispatcher = dispatcher ?? new UnityDispatcherAdapter();
    }

    private bool TryParsePort(string portString, out int port, string context = "")
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(portString))
        {
            LogHelper.LogToConsole($"[{context}] {Localization.Instance.GetText(LanguageKeys.Log_PortEmpty)}", isError: true);
            return false;
        }

        if (!int.TryParse(portString, out port))
        {
            LogHelper.LogToConsole($"[{context}] {Localization.Instance.GetText(LanguageKeys.Log_InvalidPortFormat)}: {portString}", isError: true);
            return false;
        }

        if (port < 1 || port > 65535)
        {
            LogHelper.LogToConsole($"[{context}] {Localization.Instance.GetText(LanguageKeys.Log_PortOutOfRange)}: {port}", isError: true);
            return false;
        }

        return true;
    }

    public override void AddPort(PortData portData)
    {
        if (tcpServers.TryGetValue(portData.Key, out var existingServer))
        {
            if (portData.IsConnected)
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_AlreadyConnected)}");
                return;
            }
            portData.IsConnected = false;
            existingServer.Dispose();
            tcpServers.TryRemove(portData.Key, out _);
        }

        if (!TryParsePort(portData.LocalPortDetails.Port, out int localPort, "AddPort"))
        {
            portData.IsConnected = false;
            return;
        }

        var listener = new TcpListener(IPAddress.Any, localPort);
        try
        {
            listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_PortOccupied)}: {portData.LocalPortDetails.Port}", isError: true);
            throw new InvalidOperationException($"{Localization.Instance.GetText(LanguageKeys.Log_PortOccupied)}: {portData.LocalPortDetails.Port}");
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_StartFailed)}: {ex.Message}", isError: true);
            throw new InvalidOperationException(ex.Message, ex);
        }

        var serverData = new TCPServerData
        {
            portData = portData,
            tcpListener = listener,
            CancellationTokenSource = new CancellationTokenSource(),
        };

        tcpServers[portData.Key] = serverData;
        NetworkMessageRouter.Instance.RegisterTcpServer(portData.Id, serverData);

        AcceptClientsAsync(serverData).Forget(ex =>
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_AcceptClientsError)}: {ex}", isError: true));
        ProcessPacketsAsync(serverData).Forget(ex =>
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ProcessPacketsError)}: {ex}", isError: true));

        dispatcher.Enqueue(() =>
            SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.OnUpdate"));
    }

    public override async UniTask RemovePort(PortData portData)
    {
        if (!tcpServers.TryGetValue(portData.Key, out var serverData))
            return;

        try
        {
            serverData.CancellationTokenSource?.Cancel();
            serverData.tcpListener?.Stop();
            portData.IsConnected = false;

            await UniTask.Delay(300);
            serverData.Dispose();
            tcpServers.TryRemove(portData.Key, out _);
            NetworkMessageRouter.Instance.UnregisterTcpServer(portData.Id);

            dispatcher.Enqueue(() =>
                SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.RemovePort.OnUpdate"));
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_RestartFailed)}: {ex.Message}", isError: true);
        }
    }

    public override async UniTask RestartPort(PortData portData)
    {
        await Disconnect(portData);
        await UniTask.Delay(200);
        AddPort(portData);
    }

    public override void Connect(PortData portData)
    {
        if (tcpServers.TryGetValue(portData.Key, out var serverData))
        {
            try
            {
                if (!TryParsePort(portData.LocalPortDetails.Port, out int localPort, "Connect"))
                {
                    portData.IsConnected = false;
                    return;
                }

                try { serverData.tcpListener?.Stop(); } catch { }
                serverData.tcpListener = new TcpListener(IPAddress.Any, localPort);
                serverData.tcpListener.Start();
                serverData.CancellationTokenSource?.Cancel();
                serverData.CancellationTokenSource?.Dispose();
                serverData.CancellationTokenSource = new CancellationTokenSource();
                serverData.asyncMessageQueue = new AsyncMessageQueue<(byte[], string, IPEndPoint, string)>();
                portData.IsConnected = false; // 等待 client 連入後由 AcceptClientsAsync 設為 true

                AcceptClientsAsync(serverData).Forget(ex =>
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_AcceptClientsError)}: {ex}", isError: true));
                ProcessPacketsAsync(serverData).Forget(ex =>
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ProcessPacketsError)}: {ex}", isError: true));

                dispatcher.Enqueue(() =>
                    SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.Connect.OnUpdate"));
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_RestartFailed)}: {ex.Message}", isError: true);
            }
        }
        else
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_NotFound)}", isError: true);
        }
    }

    public override async UniTask Disconnect(PortData portData)
    {
        if (tcpServers.TryGetValue(portData.Key, out var serverData))
        {
            try
            {
                serverData.CancellationTokenSource?.Cancel();
                serverData.tcpListener?.Stop();
                serverData.CancellationTokenSource?.Dispose();
                serverData.tcpListener = null;
                serverData.CancellationTokenSource = null;
                portData.IsConnected = false;

                await UniTask.Delay(300);

                dispatcher.Enqueue(() =>
                    SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.Disconnect.OnUpdate"));
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", portData)} {Localization.Instance.GetText(LanguageKeys.Log_DisconnectFailed)}: {ex.Message}", isError: true);
            }
        }
    }

    private async UniTask AcceptClientsAsync(TCPServerData serverData)
    {
        await UniTask.SwitchToThreadPool();
        var token = serverData.CancellationTokenSource.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var acceptTask = serverData.tcpListener.AcceptTcpClientAsync();
                TcpClient client;
                try
                {
                    client = await acceptTask
                        .AsUniTask(useCurrentSynchronizationContext: false).AttachExternalCancellation(token);
                }
                catch (OperationCanceledException)
                {
                    // 確保 acceptTask 的例外被 observe，避免 listener.Stop() 後觸發 UnobservedTaskException
                    _ = acceptTask.ContinueWith(t => { _ = t.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break; // Listener 已被 Dispose，正常關閉
                }
                catch (System.Net.Sockets.SocketException)
                {
                    break; // Listener.Stop() 觸發，正常關閉
                }

                if (client != null)
                {
                    if (serverData.CurrentConnections >= MAX_CONNECTIONS_PER_SERVER)
                    {
                        LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", serverData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_MaxConnections)} ({MAX_CONNECTIONS_PER_SERVER})", isError: true);
                        client?.Close();
                        continue;
                    }

                    var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                    SafeExecution.Safe(() =>
                    {
                        serverData.RemoteEndPoint = remoteEndPoint;
                        serverData.portData.IsConnected = true;

                        serverData.IncrementTotalConnections();
                        serverData.IncrementCurrentConnections();

                        serverData.portData.CurrentConnections = serverData.CurrentConnections;
                        serverData.portData.TotalConnections = serverData.TotalConnections;

                        LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", serverData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_Connected)}: {remoteEndPoint}");

                        NotifyForwardTargetStatusChange("CONNECT", serverData.portData, remoteEndPoint);
                        dispatcher.Enqueue(() =>
                            SafeExecution.Safe(() => serverData.portData.OnUpdate?.Invoke(serverData.portData)));
                    });

                    ReceiveClientAsync(client, serverData).Forget(ex =>
                        LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", serverData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_ReceiveClientError)}: {ex}", isError: true));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 監聽已取消，正常關閉
        }
    }

    private async UniTask ReceiveClientAsync(TcpClient client, TCPServerData serverData)
    {
        await UniTask.SwitchToThreadPool();
        var stream = client.GetStream();
        var token = serverData.CancellationTokenSource.Token;
        var sourceEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
        string clientKey = Guid.NewGuid().ToString("N");
        var metrics = new TcpClientMetrics(sourceEndpoint);
        serverData.ConnectedClients[clientKey] = metrics;
        serverData.ClientStreams[clientKey] = stream;
        serverData.ClientWriteLocks[clientKey] = new SemaphoreSlim(1, 1);
        byte[] buffer = new byte[2048];
        // per-client line buffer — isolates this client's partial data from all others
        var lineBuffer = new StringBuilder();

        ConfigureKeepAlive(client.Client);
        SendPingsAsync(stream, metrics, token, serverData, clientKey).Forget(ex =>
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", serverData.portData)} [Ping] {ex.Message}", isError: true));

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0)
                    break;

                metrics.RecordBytes(bytesRead);
                serverData.AddReceivedBytes(bytesRead);
                serverData.portData.TotalReceivedBytes = serverData.TotalReceivedBytes;

                string chunk;
                try
                {
                    chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
                catch (Exception)
                {
                    chunk = Encoding.GetEncoding("UTF-8", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetString(buffer, 0, bytesRead);
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", serverData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_InvalidUTF8)}", isError: true);
                }

                if (lineBuffer.Length + chunk.Length > MAX_BUFFER_SIZE)
                {
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", serverData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_BufferOverflow)} ({MAX_BUFFER_SIZE} bytes)", isError: true);
                    lineBuffer.Clear();
                    // chunk 本身也超過上限則直接捨棄，不再 Append
                    if (chunk.Length > MAX_BUFFER_SIZE) continue;
                }

                lineBuffer.Append(chunk);

                string current = lineBuffer.ToString();
                int lastNewline = current.LastIndexOf('\n');
                if (lastNewline < 0)
                    continue;

                string processable = current[..lastNewline];
                string remaining   = current[(lastNewline + 1)..];
                lineBuffer.Clear();
                lineBuffer.Append(remaining);

                foreach (var rawLine in processable.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Intercept PONG — do not route to message pipeline
                    if (metrics.TryHandlePong(line)) continue;

                    metrics.RecordMessage();
                    byte[] lineBytes = Encoding.UTF8.GetBytes(line);
                    serverData.asyncMessageQueue.Enqueue((lineBytes, line, sourceEndpoint, clientKey));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 任務被取消，正常關閉
        }
        finally
        {
            client?.Close();
            serverData.ConnectedClients.TryRemove(clientKey, out _);
            serverData.ClientStreams.TryRemove(clientKey, out _);
            if (serverData.ClientWriteLocks.TryRemove(clientKey, out var wl))
                try { wl.Dispose(); } catch (ObjectDisposedException) { }

            serverData.DecrementCurrentConnections();

            serverData.portData.IsConnected = serverData.CurrentConnections > 0;
            serverData.portData.CurrentConnections = serverData.CurrentConnections;
            serverData.portData.TotalConnections = serverData.TotalConnections;
            serverData.portData.TotalReceivedBytes = serverData.TotalReceivedBytes;

            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Server", serverData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_Disconnected)}: {sourceEndpoint}");

            NotifyForwardTargetStatusChange("DISCONNECT", serverData.portData, sourceEndpoint);
            dispatcher.Enqueue(() =>
                SafeExecution.Safe(() => serverData.portData.OnUpdate?.Invoke(serverData.portData)));
        }
    }

    private void NotifyForwardTargetStatusChange(string status, PortData sourcePortData, IPEndPoint endpoint = null)
    {
        NotifyAsync(status, sourcePortData, endpoint).Forget();
    }

    private async UniTask NotifyAsync(string status, PortData sourcePortData, IPEndPoint endpoint = null)
    {
        await UniTask.SwitchToThreadPool();

        string edgeStatus    = status == "CONNECT" ? "CONNECTED" : "DISCONNECTED";
        string endpointStr   = endpoint != null ? endpoint.ToString() : "";
        string notifyMessage = $"EDGELINK_STATUS:{edgeStatus}:{sourcePortData.ProtocolName}@{endpointStr}";
        byte[] notifyBytes   = Encoding.UTF8.GetBytes(notifyMessage + "\n");

        var targets = NetworkMessageRouter.Instance.GetTargetClients(sourcePortData.Id, sourcePortData.ProtocolName);
        foreach (var target in targets)
        {
            if (target?.tcpClient?.Connected != true) continue;
            using var cts = new CancellationTokenSource(1000);
            try
            {
                // 取得裝置寫入鎖，與 ProcessSerialQueueAsync 互斥，防止並發寫入同一條 stream
                await target.DeviceWriteLock.WaitAsync(cts.Token);
                try
                {
                    if (target.tcpClient == null || !target.tcpClient.Connected) continue;
                    var stream = target.tcpClient.GetStream();
                    await stream.WriteAsync(notifyBytes, 0, notifyBytes.Length, cts.Token);
                    LogHelper.LogToMonitor($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_NotifyTarget)} [{target.portData?.ProtocolName}]: [{sourcePortData.ProtocolName}] {status}");
                }
                finally
                {
                    try { target.DeviceWriteLock.Release(); } catch (ObjectDisposedException) { }
                }
            }
            catch (OperationCanceledException)
            {
                // 超時或 DeviceWriteLock 被取消，靜默處理
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_NotifyFailed)} [{sourcePortData.ProtocolName}] {status}: {ex.Message}", isError: true);
            }
        }
    }

    private async UniTask ProcessPacketsAsync(TCPServerData serverData)
    {
        await UniTask.SwitchToThreadPool();
        var token = serverData.CancellationTokenSource.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var (rawBytes, text, sourceEndpoint, clientKey) = await serverData.asyncMessageQueue.DequeueAsync(token);

                if (token.IsCancellationRequested || rawBytes == null)
                    break;

                await NetworkMessageRouter.Instance.RouteMessageAsync(serverData, rawBytes, text, sourceEndpoint, clientKey);
            }
        }
        catch (OperationCanceledException)
        {
            // 任務被取消，正常終止
        }
    }

    public TCPServerData GetServerData(PortData portData)
    {
        return tcpServers.TryGetValue(portData.Key, out var serverData) ? serverData : null;
    }

    private async UniTask SendPingsAsync(System.Net.Sockets.NetworkStream stream, TcpClientMetrics metrics,
        System.Threading.CancellationToken token, TCPServerData serverData, string clientKey)
    {
        await UniTask.SwitchToThreadPool();
        await UniTask.Delay(3000, cancellationToken: token);
        while (!token.IsCancellationRequested)
        {
            // 連續 3 個 PING 未收到 PONG（15s），視為設備掉線，強制關閉 stream
            // 讓 ReceiveClientAsync 的 ReadAsync 拋 IOException，觸發 finally 斷線通知
            if (metrics.IsUnresponsive(missedThreshold: 3))
            {
                try { stream.Close(); } catch { }
                break;
            }

            try
            {
                string ping = metrics.BuildPingMessage();
                byte[] bytes = Encoding.UTF8.GetBytes(ping);
                serverData.ClientWriteLocks.TryGetValue(clientKey, out var writeLock);
                if (writeLock != null) await writeLock.WaitAsync(token);
                try
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, token);
                }
                finally
                {
                    writeLock?.Release();
                }
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
            await UniTask.Delay(5000, cancellationToken: token);
        }
    }

    private static void ConfigureKeepAlive(Socket socket)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            // idle 10s 後開始探測，每 1s 探測一次，最多 3 次
            byte[] inValue = new byte[12];
            BitConverter.GetBytes(1u).CopyTo(inValue, 0);
            BitConverter.GetBytes(10_000u).CopyTo(inValue, 4);
            BitConverter.GetBytes(1_000u).CopyTo(inValue, 8);
            socket.IOControl(IOControlCode.KeepAliveValues, inValue, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TCPServer] ConfigureKeepAlive failed: {ex.Message}");
        }
    }

    public List<TcpClientInfo> GetConnectedClients(string portKey)
    {
        if (!tcpServers.TryGetValue(portKey, out var s)) return new List<TcpClientInfo>();
        return s.ConnectedClients.Values.Select(m => new TcpClientInfo
        {
            endpoint          = m.EndPoint?.ToString() ?? "",
            connectedSeconds  = (float)m.GetConnectedSeconds(),
            lastActivitySec   = (float)m.GetLastActivitySeconds(),
            messageCount      = m.GetMessageCount(),
            totalBytes        = m.GetTotalBytes(),
            rateBytesPerSec   = (float)m.GetRateBytesPerSec(),
            rttMs             = (float)m.GetLastRttMs()
        }).ToList();
    }

    public override async UniTask ShutdownAsync()
    {
        UnityEngine.Debug.Log($"[TCPServer] Shutting down {tcpServers.Count} TCP servers");

        foreach (var server in tcpServers.Values)
        {
            try
            {
                server.CancellationTokenSource?.Cancel();
                server.tcpListener?.Stop();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPServer] Error stopping listener: {ex.Message}");
            }
        }

        await UniTask.Delay(300);

        foreach (var server in tcpServers.Values)
        {
            try { server.Dispose(); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPServer] Error disposing: {ex.Message}");
            }
        }

        tcpServers.Clear();
        UnityEngine.Debug.Log("[TCPServer] All TCP servers closed");
    }
}
