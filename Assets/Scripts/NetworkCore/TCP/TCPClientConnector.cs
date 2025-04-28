using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static NetworkPortManager;

public class TCPClientConnector
{
    private readonly ConcurrentDictionary<string, TCPClientData> tcpClientDatas = new();
    private const int HeartbeatIntervalMs = 5000;
    // 可選的 callback
    public Action<PortData> OnReconnectSuccess;
    public Action<PortData> OnReconnectFailed;

    /// <summary>
    /// 新增 TCP Client
    /// </summary>
    public void AddPort(PortData portData)
    {
        if (!tcpClientDatas.ContainsKey(portData.ProtocolName))
        {
            var clientData = new TCPClientData
            {
                portData = portData,
                tcpClient = new TcpClient(),
                CancellationTokenSource = new CancellationTokenSource()
            };
            tcpClientDatas[portData.ProtocolName] = clientData;
            NetworkMessageRouter.Instance.RegisterTcpClient(portData.ProtocolName, clientData);
        }

        _ = ConnectWithRetryAsync(tcpClientDatas[portData.ProtocolName], isFirstConnect: true);
    }

    /// <summary>
    /// 主動重新連線 TCP Client
    /// </summary>
    public void Connect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData))
        {
            ResetClientConnection(clientData);
            _ = ConnectWithRetryAsync(clientData, isFirstConnect: true);
        }
        else
        {
            LogHelper.LogToConsole($"找不到 TCP Client: {portData.ProtocolName}，請先 AddPort", isError: true);
        }
    }

    /// <summary>
    /// 主動斷線 TCP Client
    /// </summary>
    public void Disconnect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData))
        {
            ResetClientConnection(clientData);
            portData.IsConnected = false;
            LogHelper.LogToConsole($"TCP Client 已完全斷線並重置: {portData.ProtocolName}");
        }
    }

    /// <summary>
    /// 移除 TCP Client
    /// </summary>
    public void RemovePort(PortData portData)
    {
        if (tcpClientDatas.TryRemove(portData.ProtocolName, out var clientData))
        {
            ResetClientConnection(clientData);
            clientData.Dispose();
            portData.IsConnected = false;
            NetworkMessageRouter.Instance.UnregisterTcpClient(portData.ProtocolName);
            LogHelper.LogToConsole($"已刪除 TCP Client: {portData.ProtocolName}");
        }
    }

    /// <summary>
    /// 重啟 TCP Client
    /// </summary>
    public void RestartPort(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData))
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

        int retryCount = 0;
        int maxRetry = isFirstConnect ? 1 : 10;
        int delayMs = 2000;
        int maxDelayMs = 30000;

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

                    // 啟動心跳之前，先確保只開一個
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
                LogHelper.LogToConsole($"TCP Client [{portData.ProtocolName}] 第 {retryCount + 1} 次連接失敗: {ex.Message}", isError: true);
            }

            retryCount++;
            await Task.Delay(delayMs, token);
            delayMs = Math.Min(delayMs * 2, maxDelayMs); // 指數回退
        }

        LogHelper.LogToConsole($"[Reconnect] TCP Client [{portData.ProtocolName}] 超過最大重試次數 {maxRetry}，停止重連。", isError: true);
        OnReconnectFailed?.Invoke(portData);
    }


    private async Task StartHeartbeatAsync(TCPClientData clientData)
    {
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;

        while (!token.IsCancellationRequested)
        {
            await Task.Delay(HeartbeatIntervalMs, token);

            if (clientData.tcpClient == null || !clientData.tcpClient.Connected)
            {
                LogHelper.LogToConsole($"[Heartbeat] 偵測到 TCP Client [{portData.ProtocolName}] 已斷線，啟動重連流程。");
                portData.IsConnected = false;

                _ = ConnectWithRetryAsync(clientData, isFirstConnect: false);
                break; // 停止目前心跳
            }

            try
            {
                // 嘗試小寫入確認連線（0-byte write）
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


    private bool ShouldStopRetry(TCPClientData clientData, int retryCount, int maxRetry)
    {
        return clientData.CancellationTokenSource.Token.IsCancellationRequested || retryCount >= maxRetry;
    }

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
                LogHelper.LogToConsole($"關閉 Stream 發生錯誤: {ex.Message}", isError: true);
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
