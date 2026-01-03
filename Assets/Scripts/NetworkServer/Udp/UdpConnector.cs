using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using System;

/// <summary>
/// UDP 連接器
/// </summary>
public class UdpConnector : NetworkConnectorBase
{
    private readonly ConcurrentDictionary<string, UdpData> udpClients = new();

    /// <summary>
    /// 安全地解析端口號
    /// </summary>
    private bool TryParsePort(string portString, out int port, string context = "")
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(portString))
        {
            LogHelper.LogToConsole($"[{context}] 端口為空", isError: true);
            return false;
        }

        if (!int.TryParse(portString, out port))
        {
            LogHelper.LogToConsole($"[{context}] 無效的端口格式: {portString}", isError: true);
            return false;
        }

        if (port < 1 || port > 65535)
        {
            LogHelper.LogToConsole($"[{context}] 端口超出範圍 (1-65535): {port}", isError: true);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 安全地解析IP地址
    /// </summary>
    private bool TryParseIP(string ipString, out IPAddress ipAddress, string context = "")
    {
        ipAddress = null;
        if (string.IsNullOrWhiteSpace(ipString))
        {
            return true; // 允許空IP（廣播模式）
        }

        if (!IPAddress.TryParse(ipString, out ipAddress))
        {
            LogHelper.LogToConsole($"[{context}] 無效的IP地址: {ipString}", isError: true);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 添加端口
    /// </summary>
    /// <param name="portData"></param>
    public override void AddPort(PortData portData)
    {
        SafeExecution.Safe(() =>
        {
            if (udpClients.TryGetValue(portData.ProtocolName, out var existingServerData))
            {
                if (portData.IsConnected)
                {
                    LogHelper.LogToConsole($"端口 {portData.RemotePortDetails.Port} 已經存在 UDP 伺服器，正在連接中。");
                    return;
                }

                SafeExecution.Safe(() => existingServerData.CancellationTokenSource?.Cancel(), "UdpConnector.CancelOldToken");
                SafeExecution.Safe(() => existingServerData.udpClient?.Dispose(), "UdpConnector.DisposeOldUdp");
                existingServerData.udpClient = null;
                portData.IsConnected = false;
                SafeExecution.Safe(() => Task.Delay(100).Wait(), "UdpConnector.WaitDispose");
            }

            try
            {
                if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "AddPort"))
                {
                    portData.IsConnected = false;
                    return;
                }

                var udpClient = new UdpClient(remotePort);
                portData.IsConnected = true;

                var newServerData = new UdpData
                {
                    portData = portData,
                    udpClient = udpClient,
                    CancellationTokenSource = new CancellationTokenSource(),
                };

                udpClients[portData.ProtocolName] = newServerData;

                LogHelper.LogToConsole($"在端口 {portData.RemotePortDetails.Port} 上啟動了 UDP 伺服器端。");

                Task.Run(() => SafeExecution.SafeAsync(() => ReceiveUdpMessages(newServerData), "UdpConnector.ReceiveUdpMessages"));

                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "UdpConnector.OnUpdate"));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                LogHelper.LogToConsole($"端口 {portData.RemotePortDetails.Port} 已經被使用。", isError: true);
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"初始化端口 {portData.RemotePortDetails.Port} 的 UDP 監聽器時發生錯誤: {ex.Message}", isError: true);
            }

        }, "UdpConnector.AddPort");
    }


    public override void Connect(PortData portData)
    {
        SafeExecution.Safe(() =>
        {
            if (udpClients.TryGetValue(portData.ProtocolName, out var udpData))
            {
                // 如果已經連接，先斷開
                if (portData.IsConnected)
                {
                    LogHelper.LogToConsole($"UDP {portData.ProtocolName} 已經連接中");
                    return;
                }

                // 驗證端口
                if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "Connect"))
                {
                    return;
                }

                try
                {
                    // 重新創建 UDP Client 和 CancellationTokenSource
                    udpData.udpClient?.Close();
                    udpData.udpClient?.Dispose();
                    udpData.udpClient = new UdpClient(remotePort);

                    udpData.CancellationTokenSource?.Cancel();
                    udpData.CancellationTokenSource?.Dispose();
                    udpData.CancellationTokenSource = new CancellationTokenSource();

                    portData.IsConnected = true;

                    // 重新啟動接收任務
                    Task.Run(() => SafeExecution.SafeAsync(() => ReceiveUdpMessages(udpData), "UdpConnector.ReceiveUdpMessages"));

                    LogHelper.LogToConsole($"UDP 已重新連接，端口 {portData.RemotePortDetails.Port}");

                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "UdpConnector.OnUpdate"));
                }
                catch (Exception ex)
                {
                    LogHelper.LogToConsole($"UDP 重新連接失敗: {ex.Message}", isError: true);
                    portData.IsConnected = false;
                }
            }
            else
            {
                LogHelper.LogToConsole($"找不到 UDP: {portData.ProtocolName}，請先新增", isError: true);
            }
        }, "UdpConnector.Connect");
    }



    /// <summary>
    /// 主動斷開連接（保留數據結構以便重新連接）
    /// </summary>
    /// <param name="portData"></param>
    public override async Task Disconnect(PortData portData)
    {
        if (!udpClients.TryGetValue(portData.ProtocolName, out var udpData))
        {
            LogHelper.LogToConsole($"未找到 UDP，端口 {portData.RemotePortDetails.Port}");
            return;
        }

        try
        {
            // 取消接收任務
            udpData.CancellationTokenSource?.Cancel();
            await Task.Delay(100); // 等待任務停止

            // 關閉 UDP Client 但不釋放 UdpData
            udpData.udpClient?.Close();
            udpData.udpClient?.Dispose();
            udpData.udpClient = null;

            // 重新創建 CancellationTokenSource 以便重新連接
            udpData.CancellationTokenSource?.Dispose();
            udpData.CancellationTokenSource = new CancellationTokenSource();

            portData.IsConnected = false;

            LogHelper.LogToConsole($"已斷開 UDP 連接，端口 {portData.RemotePortDetails.Port}");
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"斷開 UDP 連接失敗，端口 {portData.RemotePortDetails.Port}: {ex.Message}", isError: true);
        }

        UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
    }

    /// <summary>
    /// 接收 UDP 訊息
    /// </summary>
    /// <param name="udpData"></param>
    /// <returns></returns>
    private async Task ReceiveUdpMessages(UdpData udpData)
    {
        // 驗證本地端口
        if (!TryParsePort(udpData.portData.LocalPortDetails.Port, out int localPort, "ReceiveUdpMessages"))
        {
            LogHelper.LogToConsole($"UDP 接收失敗：無效的本地端口", isError: true);
            return;
        }

        // 驗證目標IP（如果有）
        if (!TryParseIP(udpData.portData.TargetIP, out IPAddress targetIP, "ReceiveUdpMessages"))
        {
            LogHelper.LogToConsole($"UDP 接收失敗：無效的目標IP", isError: true);
            return;
        }

        using var sendClient = new UdpClient();
        IPEndPoint sendEndPoint = string.IsNullOrWhiteSpace(udpData.portData.TargetIP)
            ? new IPEndPoint(IPAddress.Broadcast, localPort)
            : new IPEndPoint(targetIP, localPort);

        if (string.IsNullOrWhiteSpace(udpData.portData.TargetIP))
            sendClient.EnableBroadcast = true;

        byte[] buffer = new byte[1024];

        try
        {
            while (!udpData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                var result = await SafeExecution.WithCancellation(udpData.udpClient.ReceiveAsync(), udpData.CancellationTokenSource.Token);
                int messageLength = result.Buffer.Length;

                if (messageLength > buffer.Length)
                    buffer = new byte[messageLength];

                Array.Copy(result.Buffer, buffer, messageLength);
                string message = Encoding.UTF8.GetString(buffer, 0, messageLength);

                udpData.portData.COMReceived += messageLength;
                udpData.SourceData = message;

                LogHelper.LogToMonitor($"名稱: {udpData.portData.ProtocolName}。收到來自: {result.RemoteEndPoint} 的訊息，大小: {messageLength} bytes, 訊息: {message}");

                await sendClient.SendAsync(result.Buffer, messageLength, sendEndPoint);
                udpData.portData.NetReceived += messageLength;

                LogHelper.LogToMonitor($"名稱: {udpData.portData.ProtocolName}。傳送到: {sendEndPoint}, 大小: {messageLength} bytes, 訊息: {message}");
                UnityMainThreadDispatcher.Instance().Enqueue(() => udpData.portData.OnUpdate?.Invoke(udpData.portData));
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"接收 UDP 訊息時發生錯誤: {ex.Message}");
        }

        udpData.Dispose();
        Debug.Log($"UDP 接收器已在端口 {udpData.portData.RemotePortDetails.Port} 上關閉。");
    }

    /// <summary>
    /// 移除 UDP 端口（完全刪除）
    /// </summary>
    /// <param name="portData"></param>
    /// <returns></returns>
    public override async Task RemovePort(PortData portData)
    {
        if (udpClients.TryRemove(portData.ProtocolName, out var udpData))
        {
            try
            {
                // 取消接收任務
                udpData.CancellationTokenSource?.Cancel();
                await Task.Delay(100); // 等待任務停止

                // 完全釋放所有資源
                udpData.Dispose();
                portData.IsConnected = false;

                LogHelper.LogToConsole($"已移除 UDP 端口: {portData.ProtocolName}");

                UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"移除 UDP 端口失敗: {ex.Message}", isError: true);
            }
        }
        else
        {
            LogHelper.LogToConsole($"未找到 UDP 端口: {portData.ProtocolName}");
        }
    }

    /// <summary>
    /// 重啟 UDP 端口
    /// </summary>
    /// <param name="portData"></param>
    /// <returns></returns>
    public override async Task RestartPort(PortData portData)
    {
        await Disconnect(portData);
        await Task.Delay(200);
        AddPort(portData);
    }

    /// <summary>
    /// 關閉所有 UDP 連接
    /// </summary>
    /// <returns></returns>
    public override async Task ShutdownAsync()
    {
        UnityEngine.Debug.Log($"[UDP] 開始關閉 {udpClients.Count} 個 UDP 連接");

        // 先取消所有接收任務
        foreach (var udpData in udpClients.Values)
        {
            try
            {
                udpData.CancellationTokenSource?.Cancel();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[UDP] 取消任務時發生錯誤: {ex.Message}");
            }
        }

        // 等待一小段時間讓任務停止
        await Task.Delay(100);

        // 釋放資源
        foreach (var udpData in udpClients.Values)
        {
            try
            {
                udpData.Dispose();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[UDP] 釋放資源時發生錯誤: {ex.Message}");
            }
        }

        udpClients.Clear();
        UnityEngine.Debug.Log("[UDP] 所有 UDP 連接已關閉");
    }
}