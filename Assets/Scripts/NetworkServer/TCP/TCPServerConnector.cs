using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// TCP Server 連接器
/// </summary>
public class TCPServerConnector : NetworkConnectorBase
{
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServers = new();
    private const int MAX_CONNECTIONS_PER_SERVER = 100; // 每個 TCP Server 的最大連接數
    private const int MAX_BUFFER_SIZE = 1024 * 1024; // 最大緩衝區大小：1MB

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
    /// 添加端口
    /// </summary>
    /// <param name="portData"></param>
    public override void AddPort(PortData portData)
    {
        SafeExecution.Safe(() =>
        {
            if (tcpServers.TryGetValue(portData.ProtocolName, out var existingServer))
            {
                if (portData.IsConnected)
                {
                    LogHelper.LogToConsole($"TCP Server {portData.LocalPortDetails.Port} 已經存在並連接中。");
                    return;
                }

                portData.IsConnected = false;
                existingServer.CancellationTokenSource?.Cancel();
                existingServer.tcpListener?.Stop();
                Task.Delay(100).Wait();
            }

            try
            {
                if (!TryParsePort(portData.LocalPortDetails.Port, out int localPort, "AddPort"))
                {
                    portData.IsConnected = false;
                    return;
                }

                var listener = new TcpListener(IPAddress.Any, localPort);
                listener.Start();

                var serverData = new TCPServerData
                {
                    portData = portData,
                    tcpListener = listener,
                    CancellationTokenSource = new CancellationTokenSource(),
                };

                tcpServers[portData.ProtocolName] = serverData;
                LogHelper.LogToConsole($"在端口 {portData.LocalPortDetails.Port} 上啟動了 TCP Server。");

                Task.Run(() => AcceptClientsAsync(serverData));
                Task.Run(() => ProcessPacketsAsync(serverData));

                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.OnUpdate"));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                LogHelper.LogToConsole($"TCP Server 端口 {portData.LocalPortDetails.Port} 已經被佔用。", isError: true);
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"TCP Server 啟動錯誤: {ex.Message}", isError: true);
            }
        }, "TcpServerConnector.AddPort");
    }

    /// <summary>
    /// 刪除端口
    /// </summary>
    /// <param name="portData"></param>
    public override async Task RemovePort(PortData portData)
    {
        if (!tcpServers.TryGetValue(portData.ProtocolName, out var serverData))
        {
            LogHelper.LogToConsole($"未找到 TCP Server，無法刪除，端口 {portData.LocalPortDetails.Port}");
            return;
        }

        try
        {
            serverData.CancellationTokenSource?.Cancel();
            serverData.tcpListener?.Stop();
            portData.IsConnected = false;

            await Task.Delay(100);
            serverData.Dispose();
            tcpServers.TryRemove(portData.ProtocolName, out _);

            LogHelper.LogToConsole($"已刪除 TCP Server：{portData.ProtocolName}");

            UnityMainThreadDispatcher.Instance().Enqueue(() =>
                SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.RemovePort.OnUpdate"));
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"刪除 TCP Server 失敗，端口 {portData.LocalPortDetails.Port}: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 重啟端口
    /// </summary>
    /// <param name="portData"></param>
    public override async Task RestartPort(PortData portData)
    {
        await Task.Run(() => Disconnect(portData));
        await Task.Delay(200);
        AddPort(portData);
    }

    /// <summary>
    /// 連接 TCP Server
    /// </summary>
    /// <param name="portData"></param>
    public override void Connect(PortData portData)
    {
        if (tcpServers.TryGetValue(portData.ProtocolName, out var serverData))
        {
            try
            {
                if (!TryParsePort(portData.LocalPortDetails.Port, out int localPort, "Connect"))
                {
                    portData.IsConnected = false;
                    return;
                }

                serverData.tcpListener ??= new TcpListener(IPAddress.Any, localPort);
                serverData.tcpListener.Start();
                serverData.CancellationTokenSource?.Dispose();
                serverData.CancellationTokenSource = new CancellationTokenSource();
                portData.IsConnected = true;

                Task.Run(() => AcceptClientsAsync(serverData));
                Task.Run(() => ProcessPacketsAsync(serverData));

                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.Connect.OnUpdate"));
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"TCP Server 重新啟動失敗: {ex.Message}", isError: true);
            }
        }
        else
        {
            LogHelper.LogToConsole($"找不到 TCP Server {portData.ProtocolName}。", isError: true);
        }
    }

    /// <summary>
    /// 斷開 TCP Server 連接
    /// </summary>
    /// <param name="portData"></param>
    public override async Task Disconnect(PortData portData)
    {
        if (tcpServers.TryGetValue(portData.ProtocolName, out var serverData))
        {
            try
            {
                serverData.CancellationTokenSource?.Cancel();
                serverData.tcpListener?.Stop();
                serverData.CancellationTokenSource?.Dispose();
                serverData.tcpListener = null;
                serverData.CancellationTokenSource = null;
                portData.IsConnected = false;

                await Task.Delay(100);

                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "TcpServerConnector.Disconnect.OnUpdate"));

                LogHelper.LogToConsole($"TCP Server 已斷線: {portData.ProtocolName}");

            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"斷開 TCP Server 失敗: {ex.Message}", isError: true);
            }
        }
    }

    /// <summary>
    /// 接受客戶端連接
    /// </summary>
    /// <param name="serverData"></param>
    /// <returns></returns>
    private async Task AcceptClientsAsync(TCPServerData serverData)
    {
        var token = serverData.CancellationTokenSource.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await SafeExecution.WithCancellation(serverData.tcpListener.AcceptTcpClientAsync(), token);

                if (client != null)
                {
                    // 檢查連接數限制
                    if (serverData.CurrentConnections >= MAX_CONNECTIONS_PER_SERVER)
                    {
                        LogHelper.LogToConsole($"[安全] TCP Server {serverData.portData.LocalPortDetails.Port} 已達到最大連接數 {MAX_CONNECTIONS_PER_SERVER}，拒絕新連接", isError: true);
                        client?.Close();
                        continue;
                    }

                    SafeExecution.Safe(() =>
                    {
                        serverData.RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                        serverData.portData.IsConnected = true;

                        // 使用原子操作更新連接計數
                        serverData.IncrementTotalConnections();
                        serverData.IncrementCurrentConnections();

                        serverData.portData.CurrentConnections = serverData.CurrentConnections;
                        serverData.portData.TotalConnections = serverData.TotalConnections;

                        string serverName = serverData.portData.ProtocolName ?? "未知名稱";
                        string localPort = serverData.portData.LocalPortDetails?.Port ?? "未知端口";
                        string remoteAddress = serverData.RemoteEndPoint?.ToString() ?? "未知IP";
                        NotifyForwardTargetStatusChange("CONNECT", serverData.portData);
                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            SafeExecution.Safe(() => serverData.portData.OnUpdate?.Invoke(serverData.portData)));
                    });

                    _ = Task.Run(() => ReceiveClientAsync(client, serverData));
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log($"TCP Server {serverData.portData.LocalPortDetails.Port} 已停止監聽。");
        }
    }

    /// <summary>
    /// 接收客戶端數據
    /// </summary>
    /// <param name="client"></param>
    /// <param name="serverData"></param>
    /// <returns></returns>
    private async Task ReceiveClientAsync(TcpClient client, TCPServerData serverData)
    {
        var stream = client.GetStream();
        var token = serverData.CancellationTokenSource.Token;
        byte[] buffer = new byte[2048];

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0)
                    break;

                // 使用原子操作更新接收字節數
                serverData.AddReceivedBytes(bytesRead);

                byte[] packet = new byte[bytesRead];
                Array.Copy(buffer, 0, packet, 0, bytesRead);
                serverData.asyncMessageQueue.Enqueue(packet);
                serverData.portData.TotalReceivedBytes = serverData.TotalReceivedBytes;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            client?.Close();

            // 使用原子操作減少連接計數
            serverData.DecrementCurrentConnections();

            serverData.portData.IsConnected = serverData.CurrentConnections > 0;
            serverData.portData.CurrentConnections = serverData.CurrentConnections;
            serverData.portData.TotalConnections = serverData.TotalConnections;
            serverData.portData.TotalReceivedBytes = serverData.TotalReceivedBytes;

            string serverName = serverData.portData.ProtocolName ?? "未知名稱";
            string localPort = serverData.portData.LocalPortDetails?.Port ?? "未知端口";
            NotifyForwardTargetStatusChange("DISCONNECT", serverData.portData);
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
                SafeExecution.Safe(() => serverData.portData.OnUpdate?.Invoke(serverData.portData)));
        }
    }

    /// <summary>
    /// 通知轉發目標狀態（異步非阻塞）
    /// </summary>
    /// <param name="status"></param>
    /// <param name="sourcePortData"></param>
    private void NotifyForwardTargetStatusChange(string status, PortData sourcePortData)
    {
        // ✅ 使用 fire-and-forget 異步通知，避免阻塞主流程
        _ = Task.Run(async () =>
        {
            try
            {
                string notifyMessage = $"{status}:{sourcePortData.ProtocolName}";
                byte[] notifyBytes = Encoding.UTF8.GetBytes(notifyMessage + "\n");

                string forwardTargetProtocol = sourcePortData.ProtocolName; // TODO: 如果有更好的動態來源可以改這裡
                var targetClient = NetworkMessageRouter.Instance.GetTcpClient(forwardTargetProtocol);

                if (targetClient?.tcpClient?.Connected == true)
                {
                    var stream = targetClient.tcpClient.GetStream();

                    // ✅ 使用異步寫入 + 1秒超時
                    using var cts = new CancellationTokenSource(1000);
                    await stream.WriteAsync(notifyBytes, 0, notifyBytes.Length, cts.Token);

                    LogHelper.LogToMonitor($"[Router] 已通知目標 [{forwardTargetProtocol}]：來源 [{sourcePortData.ProtocolName}] {status}");
                }
            }
            catch (OperationCanceledException)
            {
                // 超時，靜默處理
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"[Router] 通知目標 [{sourcePortData.ProtocolName}] {status} 失敗: {ex.Message}", isError: true);
            }
        });
    }


    /// <summary>
    /// 處理接收到的數據包
    /// </summary>
    /// <param name="serverData"></param>
    /// <returns></returns>
    private async Task ProcessPacketsAsync(TCPServerData serverData)
    {
        var token = serverData.CancellationTokenSource.Token;
        var dataBuffer = new StringBuilder();

        try
        {
            while (!token.IsCancellationRequested)
            {
                byte[] packet = await serverData.asyncMessageQueue.DequeueAsync(token);

                string receivedData = Encoding.UTF8.GetString(packet);

                // 檢查緩衝區大小，防止記憶體耗盡攻擊
                if (dataBuffer.Length + receivedData.Length > MAX_BUFFER_SIZE)
                {
                    LogHelper.LogToConsole($"[安全] TCP Server {serverData.portData.LocalPortDetails.Port} 緩衝區超過限制 ({MAX_BUFFER_SIZE} 字節)，清空緩衝區", isError: true);
                    dataBuffer.Clear();
                    dataBuffer.Append(receivedData);
                }
                else
                {
                    dataBuffer.Append(receivedData);
                }

                string bufferString = dataBuffer.ToString();
                int lastNewlineIndex = bufferString.LastIndexOf('\n');

                if (lastNewlineIndex < 0)
                    continue;

                string processable = bufferString[..lastNewlineIndex];
                string remaining = bufferString[(lastNewlineIndex + 1)..];
                dataBuffer.Clear();
                dataBuffer.Append(remaining);

                foreach (var line in processable.Split('\n'))
                {
                    string message = line.Trim();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        NetworkMessageRouter.Instance.RouteMessage(serverData, packet, message);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 獲取 TCP Server 資料
    /// </summary>
    /// <param name="portData"></param>
    /// <returns></returns>
    public TCPServerData GetServerData(PortData portData)
    {
        return tcpServers.TryGetValue(portData.ProtocolName, out var serverData) ? serverData : null;
    }

    /// <summary>
    /// 關閉所有 TCP Server
    /// </summary>
    /// <returns></returns>
    public override async Task ShutdownAsync()
    {
        UnityEngine.Debug.Log($"[TCPServer] 開始關閉 {tcpServers.Count} 個 TCP Server");

        // 先取消所有任務
        foreach (var server in tcpServers.Values)
        {
            try
            {
                server.CancellationTokenSource?.Cancel();
                server.tcpListener?.Stop();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPServer] 停止監聽器時發生錯誤: {ex.Message}");
            }
        }

        // 等待一小段時間讓任務停止
        await Task.Delay(100);

        // 釋放資源
        foreach (var server in tcpServers.Values)
        {
            try
            {
                server.Dispose();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPServer] 釋放資源時發生錯誤: {ex.Message}");
            }
        }

        tcpServers.Clear();
        UnityEngine.Debug.Log("[TCPServer] 所有 TCP Server 已關閉");
    }
}
