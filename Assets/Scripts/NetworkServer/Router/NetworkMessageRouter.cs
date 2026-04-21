using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using DevKit;
using iotserver;

/// <summary>
/// 網路訊息路由器
/// </summary>
public class NetworkMessageRouter
{
    private static NetworkMessageRouter instance;
    public static NetworkMessageRouter Instance => instance ??= new NetworkMessageRouter();

    private readonly ConcurrentDictionary<string, TCPClientData> tcpClients = new();

    private NetworkMessageRouter() { }

    public void RegisterTcpClient(string protocolKey, TCPClientData clientData) =>
        tcpClients[protocolKey] = clientData;

    public void UnregisterTcpClient(string protocolKey) =>
        tcpClients.TryRemove(protocolKey, out _);

    /// <summary>
    /// 路由 TCP Server 收到的封包，套用遮罩定義後轉發（必須在 ThreadPool 上 await）
    /// </summary>
    public UniTask RouteMessageAsync(TCPServerData serverData, byte[] rawBytes, string parsedMessage)
    {
        RouterLogHelper.LogReceive(serverData.portData, MonitorTargetType.TCPServer, parsedMessage);
        return RouteAndForwardAsync(serverData.portData, rawBytes, parsedMessage);
    }

    private async UniTask RouteAndForwardAsync(PortData portData, byte[] rawBytes, string parsedMessage)
    {
        try
        {
            var targets = GetTargetClients(portData.ProtocolName);
            if (targets.Count == 0) return;

            if (targets.Count == 1)
                await ProcessAndSend(targets[0], portData.ProtocolName, rawBytes, parsedMessage);
            else
                await UniTask.WhenAll(targets.Select(t => ProcessAndSend(t, portData.ProtocolName, rawBytes, parsedMessage)));
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_UnexpectedError)}: {ex}", isError: true);
        }
    }

    // 每個 TCPClient 套用自己的遮罩後再傳送
    private async UniTask ProcessAndSend(TCPClientData client, string protocolName, byte[] rawBytes, string parsedMessage)
    {
        string maskId = client.portData?.MaskType?.Trim() ?? "OriginalData";
        var def = MaskDefinitionManager.Instance.GetDefinition(maskId)
               ?? MaskDefinitionManager.Instance.GetDefinition("OriginalData");

        if (def == null)
        {
            LogHelper.LogToConsole($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_MaskNotFound)}: '{maskId}'", isError: true);
            return;
        }

        string output = MaskProcessor.Process(def, rawBytes, parsedMessage);
        if (output == null) return;

        var bytes = Encoding.UTF8.GetBytes(output.EndsWith("\n") ? output : output + "\n");
        await TrySendToClient(client, protocolName, bytes);
    }

    private List<TCPClientData> GetTargetClients(string protocolName)
    {
        if (tcpClients.TryGetValue(protocolName, out var direct))
            return new List<TCPClientData> { direct };

        var targets = new List<TCPClientData>();
        foreach (var c in tcpClients.Values.ToList())
        {
            if (c?.portData?.ProtocolName == protocolName)
                targets.Add(c);
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

            await stream.WriteAsync(data, 0, data.Length);

            string parsedMessage = Encoding.UTF8.GetString(data);
            RouterLogHelper.LogSend(tcpClient.portData, MonitorTargetType.TCPClient, parsedMessage);
        }
        catch (OperationCanceledException)
        {
            // 寫入超時，靜默處理
        }
        catch (System.IO.IOException)
        {
            // Stream 已關閉，靜默處理
        }
        catch (System.ObjectDisposedException)
        {
            // TcpClient 已釋放，靜默處理
        }
        catch (System.InvalidOperationException)
        {
            // Stream 操作無效，靜默處理
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] {Localization.Instance.GetText(LanguageKeys.Log_ForwardFailed)} → {protocolName}: {ex.Message}", isError: true);

            try
            {
                tcpClient.tcpClient?.Close();
                tcpClient.tcpClient?.Dispose();
            }
            catch (Exception) { }

            tcpClient.tcpClient = null;
            tcpClient.portData.IsConnected = false;

            UniTask.Post(() =>
                SafeExecution.Safe(() => tcpClient.portData.OnUpdate?.Invoke(tcpClient.portData)));
        }
    }

    public TCPClientData GetTcpClient(string protocolName)
    {
        tcpClients.TryGetValue(protocolName, out var clientData);
        return clientData;
    }
}
