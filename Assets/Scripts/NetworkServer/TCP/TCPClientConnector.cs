using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// TCP Client 連線管理器
/// </summary>
public class TCPClientConnector
{
    private readonly ConcurrentDictionary<string, TCPClientData> tcpClientDatas = new();
    public Action<PortData> OnReconnectSuccess;
    public Action<PortData> OnReconnectFailed;

    /// <summary>
    /// 新增 TCP Client
    /// </summary>
    public void AddPort(PortData portData)
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
    public void Connect(PortData portData)
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
    public void Disconnect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            portData.IsConnected = false;
            LogHelper.LogToConsole($"TCP Client 已斷線: {portData.ProtocolName}");
        }
    }

    /// <summary>
    /// 移除 TCP Client
    /// </summary>
    public void RemovePort(PortData portData)
    {
        if (tcpClientDatas.TryRemove(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            clientData.Dispose();
            portData.IsConnected = false;
            NetworkMessageRouter.Instance.UnregisterTcpClient(portData.Key);
            LogHelper.LogToConsole($"已刪除 TCP Client: {portData.ProtocolName}");
        }
    }

    /// <summary>
    /// 重啟 TCP Client
    /// </summary>
    public void RestartPort(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            LogHelper.LogToConsole($"重新啟動 TCP Client: {portData.ProtocolName}");
            Disconnect(portData);
            _ = ConnectWithRetryAsync(clientData, isFirstConnect: true);
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
    public async Task ShutdownAsync()
    {
        var tasks = tcpClientDatas.Values.Select(clientData => Task.Run(() =>
        {
            ResetClientConnection(clientData);
            clientData.Dispose();
        })).ToList();

        tcpClientDatas.Clear();
        await Task.WhenAll(tasks);
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
                // 先安全 Close 舊的 tcpClient
                clientData.tcpClient?.Close();
                clientData.tcpClient?.Dispose();
                clientData.tcpClient = new TcpClient();
                var connectTask = clientData.tcpClient.ConnectAsync(portData.TargetIP, int.Parse(portData.RemotePortDetails.Port));
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

                    UnityMainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData));
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
            catch (Exception ex)
            {
                portData.IsConnected = false;

                UnityMainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData)); // ✅ 加這行通知 UI
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

        while (!token.IsCancellationRequested)
        {
            var cfg = NetworkPortManager.Instance.GetTcpClientRetryConfig();
            await Task.Delay(cfg.HeartbeatIntervalMs, token);

            if (IsSocketDisconnected(clientData.tcpClient))
            {
                LogHelper.LogToConsole($"[Heartbeat] TCP Client [{portData.ProtocolName}] socket 判斷為斷線，啟動重連流程。");
                portData.IsConnected = false;
                UnityMainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData)); // 告知 UI
                _ = ConnectWithRetryAsync(clientData, isFirstConnect: false);
                break;
            }

            try
            {
                var stream = clientData.tcpClient.GetStream();
                if (stream.CanWrite)
                {
                    await stream.WriteAsync(Array.Empty<byte>(), 0, 0, token); // 0-byte Ping
                }
                else
                {
                    throw new Exception("Stream 不可寫入");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"[Heartbeat] TCP Client [{portData.ProtocolName}] 心跳失敗: {ex.Message}", isError: true);
                portData.IsConnected = false;

                _ = ConnectWithRetryAsync(clientData, isFirstConnect: false);
                break; // 停止目前心跳
            }
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
        catch
        {
            return true;
        }
    }


    /// <summary>
    /// 重置 TCP Client 連線
    /// </summary>
    /// <param name="clientData"></param>
    private void ResetClientConnection(TCPClientData clientData)
    {
        try
        {
            clientData.CancellationTokenSource?.Cancel();

            try
            {
                var stream = clientData.tcpClient?.GetStream();
                stream?.Close();
                stream?.Dispose();
            }
            catch (Exception ex)
            {
            }

            clientData.tcpClient?.Close();
            clientData.tcpClient?.Dispose();
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"重置 TcpClient 發生錯誤: {ex.Message}", isError: true);
        }

        clientData.CancellationTokenSource = new CancellationTokenSource();
        clientData.tcpClient = new TcpClient();
    }
}
