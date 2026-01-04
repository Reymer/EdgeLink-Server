using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// TCP Client 連線管理器
/// </summary>
public class TCPClientConnector : NetworkConnectorBase
{
    private readonly ConcurrentDictionary<string, TCPClientData> tcpClientDatas = new();
    public Action<PortData> OnReconnectSuccess;
    public Action<PortData> OnReconnectFailed;

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
    /// 新增 TCP Client
    /// </summary>
    public override void AddPort(PortData portData)
    {
        if (!tcpClientDatas.ContainsKey(portData.Key))
        {
            var clientData = new TCPClientData
            {
                portData = portData,
                tcpClient = new TcpClient(),
                CancellationTokenSource = new CancellationTokenSource()
            };
            tcpClientDatas[portData.Key] = clientData;
            NetworkMessageRouter.Instance.RegisterTcpClient(portData.Key, clientData);
        }

        _ = ConnectWithRetryAsync(tcpClientDatas[portData.Key], isFirstConnect: true);
    }

    /// <summary>
    /// 主動重新連線 TCP Client
    /// </summary>
    public override void Connect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            _ = ConnectWithRetryAsync(clientData, isFirstConnect: true);
        }
        else
        {
            LogHelper.LogToConsole($"找不到 TCP Client: {portData.ProtocolName}，請先新增", isError: true);
        }
    }

    /// <summary>
    /// 主動斷線 TCP Client
    /// </summary>
    public override Task Disconnect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            portData.IsConnected = false;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 移除 TCP Client
    /// </summary>
    public override Task RemovePort(PortData portData)
    {
        if (tcpClientDatas.TryRemove(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            clientData.Dispose();
            portData.IsConnected = false;
            NetworkMessageRouter.Instance.UnregisterTcpClient(portData.Key);
            LogHelper.LogToConsole($"已刪除 TCP Client: {portData.ProtocolName}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重啟 TCP Client
    /// </summary>
    public override async Task RestartPort(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            LogHelper.LogToConsole($"重新啟動 TCP Client: {portData.ProtocolName}");
            await Disconnect(portData);
            await ConnectWithRetryAsync(clientData, isFirstConnect: true);
        }
    }

    /// <summary>
    /// 取得 TCP Client 資料
    /// </summary>
    public TCPClientData GetClientData(PortData portData)
    {
        return tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData) ? clientData : null;
    }

    /// <summary>
    /// 關閉所有 TCP Client
    /// </summary>
    public override async Task ShutdownAsync()
    {
        UnityEngine.Debug.Log($"[TCPClient] 開始關閉 {tcpClientDatas.Count} 個 TCP Client");

        // 先取消所有重連任務
        foreach (var clientData in tcpClientDatas.Values)
        {
            try
            {
                clientData.CancellationTokenSource?.Cancel();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPClient] 取消任務時發生錯誤: {ex.Message}");
            }
        }

        // 等待一小段時間讓任務停止
        await Task.Delay(100);

        // 釋放資源
        foreach (var clientData in tcpClientDatas.Values)
        {
            try
            {
                ResetClientConnection(clientData);
                clientData.Dispose();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPClient] 釋放資源時發生錯誤: {ex.Message}");
            }
        }

        tcpClientDatas.Clear();
        UnityEngine.Debug.Log("[TCPClient] 所有 TCP Client 已關閉");
    }

    /// <summary>
    /// 核心：自動連線與重連邏輯
    /// </summary>
    private async Task ConnectWithRetryAsync(TCPClientData clientData, bool isFirstConnect)
    {
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;
        var cfg = NetworkPortManager.Instance.GetTcpClientRetryConfig();
        int retryCount = 0;
        int maxRetry = isFirstConnect ? cfg.MaxRetryFirst : cfg.MaxRetrySubsequent;
        int delayMs = cfg.InitialDelayMs;

        while (!ShouldStopRetry(clientData, retryCount, maxRetry))
        {
            try
            {
                // 驗證遠端端口
                if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "ConnectWithRetry"))
                {
                    portData.IsConnected = false;
                    LogHelper.LogToConsole($"TCP Client [{portData.ProtocolName}] 遠端端口無效，停止重連。", isError: true);
                    return;
                }

                // 先安全 Close 舊的 tcpClient
                clientData.tcpClient?.Close();
                clientData.tcpClient?.Dispose();
                clientData.tcpClient = new TcpClient();
                var connectTask = clientData.tcpClient.ConnectAsync(portData.TargetIP, remotePort);
                var timeoutTask = Task.Delay(5000, token);

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    // 超時要自己關掉
                    clientData.tcpClient?.Close();
                    clientData.tcpClient = null;
                    throw new TimeoutException("TCP connect timeout");
                }

                if (clientData.tcpClient?.Connected == true)
                {
                    // 進一步檢查 Stream 是否能寫
                    var stream = clientData.tcpClient.GetStream();
                    if (stream == null || !stream.CanWrite)
                        throw new Exception("TCP Stream 不可寫入，視為連線失敗");

                    portData.IsConnected = true;
                    LogHelper.LogToConsole($"TCP Client [{portData.ProtocolName}] 成功連接到 {portData.TargetIP}:{portData.RemotePortDetails.Port}");
                    MainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData));
                    OnReconnectSuccess?.Invoke(portData);
                    clientData.HeartbeatTask?.Dispose();
                    clientData.HeartbeatTask = StartHeartbeatAsync(clientData);


                    return; // 成功連線，結束
                }
                else
                {
                    throw new Exception("TCP Connect失敗");
                }
            }
            catch (TimeoutException)
            {
                // ✅ P1.4: 連接超時，準備重試（不記錄，避免日誌過多）
                portData.IsConnected = false;
                MainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (SocketException ex)
            {
                // ✅ P1.4: Socket 異常，記錄錯誤碼
                portData.IsConnected = false;
                LogHelper.LogToConsole($"[ConnectWithRetry] TCP Client [{portData.ProtocolName}] Socket錯誤: {ex.SocketErrorCode}");
                MainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (Exception ex)
            {
                // ✅ P1.4: 其他異常，只記錄訊息而非完整堆疊
                portData.IsConnected = false;
                LogHelper.LogToConsole($"[ConnectWithRetry] TCP Client [{portData.ProtocolName}] 連接失敗: {ex.Message}");
                MainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }


            retryCount++;
            await Task.Delay(delayMs, token);
        }

        LogHelper.LogToConsole($"[Reconnect] TCP Client [{portData.ProtocolName}] 超過最大重試次數 {maxRetry}，停止重連。", isError: true);
        OnReconnectFailed?.Invoke(portData);
    }

    /// <summary>
    /// 啟動心跳檢查
    /// </summary>
    /// <param name="clientData"></param>
    /// <returns></returns>

    private async Task StartHeartbeatAsync(TCPClientData clientData)
    {
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var cfg = NetworkPortManager.Instance.GetTcpClientRetryConfig();
                await Task.Delay(cfg.HeartbeatIntervalMs, token);

                if (IsSocketDisconnected(clientData.tcpClient))
                {
                    LogHelper.LogToConsole($"[Heartbeat] TCP Client [{portData.ProtocolName}] socket 判斷為斷線，啟動重連流程。");
                    portData.IsConnected = false;
                    MainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData));
                    _ = ConnectWithRetryAsync(clientData, isFirstConnect: false);
                    break;
                }

                try
                {
                    var stream = clientData.tcpClient.GetStream();
                    if (stream.CanWrite)
                    {
                        await stream.WriteAsync(Array.Empty<byte>(), 0, 0, token);
                    }
                    else
                    {
                        throw new Exception("Stream 不可寫入");
                    }
                }
                catch (OperationCanceledException)
                {
                    // ✅ 取消操作是正常的，直接跳出
                    break;
                }
                catch (Exception ex)
                {
                    LogHelper.LogToConsole($"[Heartbeat] TCP Client [{portData.ProtocolName}] 心跳失敗: {ex.Message}", isError: true);
                    portData.IsConnected = false;

                    _ = ConnectWithRetryAsync(clientData, isFirstConnect: false);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ✅ 心跳任務被取消（正常關閉流程，不需要日誌）
        }
        catch (Exception ex)
        {
            // ✅ 記錄未預期的異常
            LogHelper.LogToConsole($"[Heartbeat] TCP Client [{portData.ProtocolName}] 心跳任務異常: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 檢查是否應該停止重試
    /// </summary>
    /// <param name="clientData"></param>
    /// <param name="retryCount"></param>
    /// <param name="maxRetry"></param>
    /// <returns></returns>
    private bool ShouldStopRetry(TCPClientData clientData, int retryCount, int maxRetry)
    {
        if (clientData.CancellationTokenSource.Token.IsCancellationRequested)
            return true;

        // 無限重試條件（-1 或 int.MaxValue）
        if (maxRetry < 0 || maxRetry == int.MaxValue)
            return false;

        return retryCount >= maxRetry;
    }


    /// <summary>
    /// 檢查 Socket 是否已斷線
    /// </summary>
    /// <param name="client"></param>
    /// <returns></returns>
    private bool IsSocketDisconnected(TcpClient client)
    {
        try
        {
            if (client == null || !client.Connected) return true;

            Socket socket = client.Client;
            return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch (ObjectDisposedException)
        {
            // ✅ P1.3: Socket 已被釋放，視為已斷線
            return true;
        }
        catch (SocketException ex)
        {
            // ✅ P1.3: Socket 異常，記錄並視為已斷線
            LogHelper.LogToConsole($"[IsSocketDisconnected] SocketException: {ex.SocketErrorCode}");
            return true;
        }
        catch (Exception ex)
        {
            // ✅ P1.3: 未預期的異常，記錄並視為已斷線
            LogHelper.LogToConsole($"[IsSocketDisconnected] 未預期異常: {ex.Message}", isError: true);
            return true;
        }
    }


    /// <summary>
    /// 重置 TCP Client 連線
    /// </summary>
    /// <param name="clientData"></param>
    private void ResetClientConnection(TCPClientData clientData)
    {
        // ✅ P0.4 修復：正確釋放資源，避免泄漏
        try
        {
            // 1. 取消並釋放舊的 CancellationTokenSource
            if (clientData.CancellationTokenSource != null)
            {
                try
                {
                    clientData.CancellationTokenSource.Cancel();
                    clientData.CancellationTokenSource.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 已釋放，忽略
                }
            }

            // 2. 關閉並釋放 TcpClient（會自動釋放 Stream）
            try
            {
                clientData.tcpClient?.Close();
                clientData.tcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"關閉 TcpClient 時發生錯誤: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"重置 TcpClient 發生錯誤: {ex.Message}", isError: true);
        }
        finally
        {
            // 3. 創建新的實例
            clientData.CancellationTokenSource = new CancellationTokenSource();
            clientData.tcpClient = new TcpClient();
        }
    }
}
