using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System;
using static NetworkPortManager;
using System.Collections.Generic;

public class TcpClientConnector
{
    private readonly ConcurrentDictionary<string, TCPClientData> tcpClientDatas = new();

    /// <summary>
    /// 新增 TCP Client 連線
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

        _ = ConnectTcpClient(tcpClientDatas[portData.ProtocolName]);
    }

    /// <summary>
    /// 主動連線 TCP Client
    /// </summary>
    /// <param name="portData"></param>
    public void Connect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData))
        {
            LogHelper.LogToConsole($"手動重新連線 TCP Client: {portData.ProtocolName}");

            try
            {
                if (!clientData.CancellationTokenSource.IsCancellationRequested)
                    clientData.CancellationTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"取消舊 CancellationToken 時出錯: {ex.Message}", isError: true);
            }

            clientData.CancellationTokenSource = new CancellationTokenSource();

            try
            {
                clientData.tcpClient?.Close();
                clientData.tcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"關閉舊 TcpClient 時出錯: {ex.Message}", isError: true);
            }

            clientData.tcpClient = new TcpClient();
            clientData.portData.IsConnected = false;

            _ = ConnectTcpClient(clientData);
        }
        else
        {
            LogHelper.LogToConsole($"找不到 TCP Client: {portData.ProtocolName}，請先 AddPort", isError: true);
        }
    }

    /// <summary>
    /// 主動斷線 TCP Client
    /// </summary>
    /// <param name="portData"></param>
    public void Disconnect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData))
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
                catch (Exception streamEx)
                {
                    LogHelper.LogToConsole($"關閉 Stream 發生錯誤: {streamEx.Message}", isError: true);
                }

                clientData.tcpClient?.Close();
                clientData.tcpClient?.Dispose();
                clientData.CancellationTokenSource = new CancellationTokenSource();
                clientData.tcpClient = new TcpClient();
                portData.IsConnected = false;

                LogHelper.LogToConsole($"TCP Client 已完全斷線並重置: {portData.ProtocolName}");
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"斷線 TCP Client 失敗: {ex.Message}", isError: true);
            }
        }
    }

    /// <summary>
    /// 移除 TCP Client 連線
    /// </summary>
    /// <param name="portData"></param>
    public void RemovePort(PortData portData)
    {
        if (tcpClientDatas.TryRemove(portData.ProtocolName, out var clientData))
        {
            try
            {
                clientData.CancellationTokenSource?.Cancel();
                clientData.tcpClient?.Close();
                clientData.Dispose();
                portData.IsConnected = false;
                NetworkMessageRouter.Instance.UnregisterTcpClient(portData.ProtocolName);
                LogHelper.LogToConsole($"已刪除 TCP Client: {portData.ProtocolName}");
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"移除 TCP Client 失敗: {ex.Message}", isError: true);
            }
        }
    }

    /// <summary>
    /// 重新啟動 TCP Client 連線
    /// </summary>
    /// <param name="portData"></param>
    public void RestartPort(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData))
        {
            LogHelper.LogToConsole($"重新啟動 TCP Client: {portData.ProtocolName}");
            Disconnect(portData);
            _ = ConnectTcpClient(clientData);
        }
    }

    /// <summary>
    /// 獲取 TCP Client 資料
    /// </summary>
    /// <param name="portData"></param>
    /// <returns></returns>
    public TCPClientData GetClientData(PortData portData)
    {
        return tcpClientDatas.TryGetValue(portData.ProtocolName, out var clientData) ? clientData : null;
    }

    /// <summary>
    /// 連接 TCP Client
    /// </summary>
    /// <param name="clientData"></param>
    /// <returns></returns>

    public async Task ConnectTcpClient(TCPClientData clientData)
    {
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;
        int retryDelay = 5000;

        while (!token.IsCancellationRequested)
        {
            try
            {
                clientData.tcpClient = new TcpClient();
                var connectTask = clientData.tcpClient.ConnectAsync(portData.TargetIP, int.Parse(portData.RemotePortDetails.Port));
                var timeout = Task.Delay(1000, token);

                if (await Task.WhenAny(connectTask, timeout) == timeout)
                    throw new TimeoutException("TCP connect timeout");

                if (!clientData.tcpClient.Connected)
                    throw new SocketException();

                portData.IsConnected = true;

                string clientName = portData.ProtocolName ?? "未知名稱";
                string targetIP = portData.TargetIP ?? "未知IP";
                string targetPort = portData.RemotePortDetails?.Port ?? "未知Port";

                LogHelper.LogToConsole($"TCP Client [{clientName}] 成功連接到 {targetIP}:{targetPort}");

                break;
            }
            catch (Exception ex)
            {
                portData.IsConnected = false;

                string clientName = portData.ProtocolName ?? "未知名稱";
                string targetIP = portData.TargetIP ?? "未知IP";
                string targetPort = portData.RemotePortDetails?.Port ?? "未知Port";

                LogHelper.LogToConsole($"TCP Client [{clientName}] 連接 {targetIP}:{targetPort} 失敗: {ex.Message}", isError: true);

                await Task.Delay(retryDelay, token);
            }
        }

        UnityMainThreadDispatcher.Instance()?.Enqueue(() => portData.OnUpdate?.Invoke(portData));
    }


    /// <summary>
    /// 關閉所有 TCP Client 連線
    /// </summary>
    /// <returns></returns>
    public async Task ShutdownAsync()
    {
        var tasks = new List<Task>();
        foreach (var clientData in tcpClientDatas.Values)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    clientData.CancellationTokenSource?.Cancel();
                    clientData.tcpClient?.Close();
                    clientData.Dispose();
                }
                catch (Exception ex)
                {
                    LogHelper.LogToConsole($"關閉 TCP Client 失敗: {ex.Message}", isError: true);
                }
            }));
        }
        tcpClientDatas.Clear();
        await Task.WhenAll(tasks);
    }
}
