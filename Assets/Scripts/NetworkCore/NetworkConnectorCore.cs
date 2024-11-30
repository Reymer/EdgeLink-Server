using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;
using UnityEngine;
using DevKit.Console;
using static NetworkPortManager;
using System.Buffers;
using System.Linq;

public class NetworkConnectorCore
{
    #region 宣告
    private ConsoleUI consoleUI;
    private MonitorConsole monitorConsole;
    private PortData portData;
    #endregion

    #region 資料結構 TCP Client、TCP Server、UDP
    public class UdpData : IDisposable
    {
        public UdpClient udpClient;
        public CancellationTokenSource CancellationTokenSource = new();
        public PortData portData;
        private readonly object lockObj = new();
        public bool IsConnecting;
        public string SourceData = string.Empty;
        private bool disposed = false;
        public void Dispose()
        {
            DisposeResources(ref disposed, lockObj, () =>
            {
                CancellationTokenSource?.Cancel();
                CancellationTokenSource?.Dispose();
                CancellationTokenSource = null;

                udpClient.Close();
                udpClient.Dispose();
                udpClient = null;
            });
        }
    }

    public class TCPServerData : IDisposable
    {
        public TcpListener tcpListener;
        public CancellationTokenSource CancellationTokenSource = new();
        public PortData portData;
        public string sourceData = string.Empty;
        public bool disposed = false;
        private readonly object lockObj = new();
        public void Dispose()
        {
            DisposeResources(ref disposed, lockObj, () =>
            {
                CancellationTokenSource?.Cancel();
                CancellationTokenSource?.Dispose();
                CancellationTokenSource = null;

                tcpListener?.Stop();
                tcpListener = null;
            });
        }
    }

    public class TCPClientData : IDisposable
    {
        public TcpClient tcpClient;
        public CancellationTokenSource CancellationTokenSource = new();
        private bool disposed = false;
        private readonly object lockObj = new();
        public PortData portData;
        public bool IsConnecting;
        public void Dispose()
        {
            DisposeResources(ref disposed, lockObj, () =>
            {
                CancellationTokenSource?.Cancel();
                CancellationTokenSource?.Dispose();
                CancellationTokenSource = null;

                tcpClient?.Close();
                tcpClient?.Dispose();
                tcpClient = null;
            });
        }
    }

    #region 私有幫助方法

    private static void DisposeResources(ref bool disposed, object lockObj, Action disposeAction)
    {
        lock (lockObj)
        {
            if (disposed) return;
            disposed = true;
            disposeAction();
        }
    }

    #endregion

    public void Init(ConsoleUI consoleUI, MonitorConsole monitorConsole)
    {
        this.consoleUI = consoleUI;
        this.monitorConsole = monitorConsole;
    }

    #endregion

    #region 資料字典存取

    public enum ConnectionType { UDP, TCP }
    private readonly ConcurrentDictionary<string, UdpData> udpClients = new();
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServerdatas = new();
    private readonly ConcurrentDictionary<string, TCPClientData> tcpClientdatas = new();

    #endregion

    #region 新增 TCP 或 UDP 連線邏輯

    /// <summary>
    /// 單獨新增 TCP Server、TCP Client 或 UDP 服務
    /// </summary>
    /// <param name="remotePort">要新增的端口</param>
    /// <param name="connectionType">連接類型 (TCP 或 UDP)</param>
    public void AddPort(PortData portData)
    {
        switch (portData.NetProtocol.ToUpperInvariant())
        {
            case "UDP":
                AddUdpClient(portData);
                break;
            case "TCP CLIENT":
                AddTcpClient(portData);
                break;
            case "TCP SERVER":
                AddTcpListener(portData);
                break;
            default:
                LogOnMainThread($"無法識別的連接類型: {portData.NetProtocol}", isError: true);
                break;
        }
    }

    /// <summary>
    /// 單獨斷線 TCP Server、TCP Client 或 UDP 服務
    /// </summary>
    /// <param name="portData"></param>
    public void Disconnected(PortData portData)
    {
        switch (portData.NetProtocol.ToUpperInvariant())
        {
            case "UDP":
                DisconnectedUdp(portData);
                break;
            case "TCP CLIENT":
                DisconnectedTcpClient(portData);
                break;
            case "TCP SERVER":
                DisconnectTcpServer(portData);
                break;
            default:
                LogOnMainThread($"無法識別的連接類型: {portData.NetProtocol}", isError: true);
                break;
        }
    }

    /// <summary>
    /// 主動斷線 UDP
    /// </summary>
    /// <param name="portData"></param>
    public async void DisconnectedUdp(PortData portData)
    {
        if (!udpClients.ContainsKey(portData.ProtocolName))
        {
            LogOnMainThread($"未找到 UDP 伺服器，端口 {portData.RemotePortDetails.Port}");
            return;
        }

        var udpData = udpClients[portData.ProtocolName];

        if (portData.IsConnected && udpData.udpClient != null)
        {
            try
            {
                udpData.CancellationTokenSource.Cancel();
                udpData.IsConnecting = false;
                portData.IsConnected = false;

                await Task.Delay(100);
                udpData.Dispose();

                LogOnMainThread($"已主動斷開 UDP 連接，端口 {portData.RemotePortDetails.Port}");
            }
            catch (Exception ex)
            {
                LogOnMainThread($"主動斷開連接失敗，端口 {portData.RemotePortDetails.Port}: {ex.Message}");
            }
            finally
            {
                udpData.CancellationTokenSource = new CancellationTokenSource();
            }
        }
        UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
    }

    /// <summary>
    /// 主動斷線 TCP Client
    /// </summary>
    /// <param name="portData"></param>
    public async void DisconnectedTcpClient(PortData portData)
    {
        if (!tcpClientdatas.ContainsKey(portData.ProtocolName))
        {
            LogOnMainThread($"未找到 TCP 客户端，端口 {portData.RemotePortDetails.Port}");
            return;
        }
        else
        {
            var tcpClientData = tcpClientdatas[portData.ProtocolName];
            try
            {
                tcpClientData.CancellationTokenSource?.Cancel();
                tcpClientData.CancellationTokenSource?.Dispose();
                tcpClientData.CancellationTokenSource = null;
                tcpClientData.IsConnecting = false;
                portData.IsConnected = false;
                await Task.Delay(100);
                tcpClientData.Dispose();

                LogOnMainThread($"已主動斷開 TCP 連接，端口 {portData.RemotePortDetails.Port}");
            }
            catch (Exception ex)
            {
                LogOnMainThread($"主動斷開連接失敗，端口 {portData.RemotePortDetails.Port}: {ex.Message}");
            }
            finally
            {
                tcpClientData.CancellationTokenSource = new CancellationTokenSource();
            }
            UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
    }

    /// <summary>
    /// 新增 TCP 客戶端
    /// </summary>
    /// <param name="portData"></param>
    private async void AddTcpClient(PortData portData)
    {
        if (tcpClientdatas.TryGetValue(portData.ProtocolName, out var tcpClientData))
        {
            if (tcpClientData.tcpClient != null && tcpClientData.IsConnecting && tcpClientData.tcpClient.Connected)
            {
                LogOnMainThread($"端口 {portData.RemotePortDetails.Port} 的 TCP 客戶端已經連接。");
                return;
            }
            else
            {
                LogOnMainThread($"端口 {portData.RemotePortDetails.Port} 的 TCP 客戶端尚未連接或已斷開，正在重新連接...");
            }
        }
        else
        {
            tcpClientData = new TCPClientData
            {
                portData = portData,
                tcpClient = new TcpClient(),
                CancellationTokenSource = new CancellationTokenSource()
            };
            tcpClientdatas.TryAdd(portData.ProtocolName, tcpClientData);
            LogOnMainThread($"在端口 {portData.RemotePortDetails.Port} 上啟動了 TCP 客戶端。");
        }

        await ConnectTcpClient(tcpClientData, portData);
    }

    /// <summary>
    /// 主動連線 TCP Client
    /// </summary>
    /// <param name="tcpClientData"></param>
    /// <param name="portData"></param>
    /// <returns></returns>
    private async Task ConnectTcpClient(TCPClientData tcpClientData, PortData portData)
    {
        var token = tcpClientData.CancellationTokenSource.Token;
        int reconnectDelay = 5000;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var tcpClient = new TcpClient();

                if (token.IsCancellationRequested)
                {
                    LogOnMainThread($"TCP 客戶端連接已被取消，目標端口: {portData.RemotePortDetails.Port}");
                    return;
                }

                var connectTask = tcpClient.ConnectAsync(portData.TargetIP, int.Parse(portData.RemotePortDetails.Port));
                var timeout = Task.Delay(1000, token);
                var completedTask = await Task.WhenAny(connectTask, timeout);

                if (completedTask == timeout || connectTask.IsFaulted)
                {
                    portData.IsConnected = false;
                    tcpClientData.IsConnecting = false;
                    LogOnMainThread($"TCP 客戶端連接超時或失敗，目標端口: {portData.RemotePortDetails.Port}");
                    UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
                    await Task.Delay(reconnectDelay);
                    continue;
                }
                tcpClientData.tcpClient = tcpClient;
                portData.IsConnected = true;
                tcpClientData.IsConnecting = true;
                int localPort = ((IPEndPoint)tcpClient.Client.LocalEndPoint).Port;
                LogOnMainThread($"成功連接到 TCP 客戶端，遠端 IP: {portData.TargetIP}，端口: {portData.RemotePortDetails.Port}，客戶端傳送的本地端口: {localPort}");
                StartConnectionMonitoring(tcpClientData, portData);
                break;
            }
            catch (Exception ex)
            {
                portData.IsConnected = false;
                tcpClientData.IsConnecting = false;
                LogOnMainThread($"TCP 客戶端連接失敗，目標 IP: {portData.TargetIP}:{portData.RemotePortDetails.Port}，錯誤: {ex.Message}");
                UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
                await Task.Delay(reconnectDelay);
            }
        }

        UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
    }

    private void StartConnectionMonitoring(TCPClientData tcpClientData, PortData portData)
    {
        var token = tcpClientData.CancellationTokenSource.Token;
        int reconnectDelay = 5000;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000);

                    if (tcpClientData.tcpClient == null || !tcpClientData.tcpClient.Connected ||
                        (tcpClientData.tcpClient.Client.Poll(0, SelectMode.SelectRead) && tcpClientData.tcpClient.Client.Available == 0))
                    {
                        LogOnMainThread($"TCP 客戶端連接已斷開，目標端口: {portData.RemotePortDetails.Port}");

                        tcpClientData.tcpClient?.Close();
                        tcpClientData.tcpClient = null;
                        portData.IsConnected = false;
                        tcpClientData.IsConnecting = false;
                        UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));

                        while (!token.IsCancellationRequested)
                        {
                            LogOnMainThread($"嘗試重新連接到 TCP 客戶端，目標端口: {portData.RemotePortDetails.Port}");

                            try
                            {
                                var reconnectClient = new TcpClient();
                                var reconnectTask = reconnectClient.ConnectAsync(portData.TargetIP, int.Parse(portData.RemotePortDetails.Port));
                                var timeoutTask = Task.Delay(reconnectDelay, token);
                                var completedTask = await Task.WhenAny(reconnectTask, timeoutTask);

                                if (completedTask == reconnectTask && reconnectClient.Connected)
                                {
                                    LogOnMainThread($"重新連接成功，目標端口: {portData.RemotePortDetails.Port}");
                                    tcpClientData.tcpClient = reconnectClient;
                                    portData.IsConnected = true;
                                    tcpClientData.IsConnecting = true;
                                    UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
                                    break;
                                }
                            }
                            catch (Exception reconnectEx)
                            {
                                LogOnMainThread($"重連失敗，目標端口: {portData.RemotePortDetails.Port}，錯誤: {reconnectEx.Message}");
                            }

                            await Task.Delay(reconnectDelay);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnMainThread($"TCP 客戶端連接狀態檢測時發生異常: {ex.Message}");
                portData.IsConnected = false;
                tcpClientData.IsConnecting = false;
                UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
        }, token);
    }


    private void AddUdpClient(PortData portData)
    {
        if (udpClients.TryGetValue(portData.ProtocolName, out var existingServerData))
        {
            if (existingServerData.IsConnecting)
            {
                LogOnMainThread($"端口 {portData.RemotePortDetails.Port} 已經存在 UDP 伺服器，正在連接中。");
                return;
            }

            try
            {
                existingServerData.CancellationTokenSource?.Cancel();
                existingServerData.udpClient?.Dispose();
                existingServerData.udpClient = null;
                existingServerData.IsConnecting = false;
                LogOnMainThread($"端口 {portData.RemotePortDetails.Port} 的 UDP 伺服器已停止，準備重新啟動。");
                Task.Delay(100).Wait();
            }
            catch (Exception ex)
            {
                LogOnMainThread($"停止端口 {portData.RemotePortDetails.Port} 的 UDP 伺服器時發生錯誤: {ex.Message}", isError: true);
            }
        }

        try
        {
            var udpClient = new UdpClient(int.Parse(portData.RemotePortDetails.Port));
            portData.IsConnected = true;
            existingServerData = new UdpData
            {
                portData = portData,
                udpClient = udpClient,
                CancellationTokenSource = new CancellationTokenSource(),
            };

            udpClients[portData.ProtocolName] = existingServerData;

            LogOnMainThread($"在端口 {portData.RemotePortDetails.Port} 上啟動了 UDP 伺服器端。");

            Task.Run(() => ReceiveUdpMessages(portData, existingServerData));

            UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            LogOnMainThread($"端口 {portData.RemotePortDetails.Port} 已經被使用。", isError: true);
        }
        catch (Exception ex)
        {
            LogOnMainThread($"初始化端口 {portData.RemotePortDetails.Port} 的 UDP  監聽器時發生錯誤: {ex.Message}", isError: true);
        }
    }


    #endregion

    #region 停止 TCP 或 UDP 連線邏輯

    /// <summary>
    /// 停止指定端口上的 TCP 或 UDP 客戶端，並刪除相關資料
    /// </summary>
    /// <param name="port">端口號</param>
    /// <param name="connectionType">連接類型，"TCP" 或 "UDP"</param>
    public void StopClient(PortData portData)
    {
        try
        {
            if (portData.NetProtocol.Equals("UDP", StringComparison.OrdinalIgnoreCase))
            {
                DisconnectedUdp(portData);
            }
            else if (portData.NetProtocol.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
            {
                DisposeTcpServer(portData);
            }
            else if (portData.NetProtocol.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
            {
                DisconnectedTcpClient(portData);
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"停止客戶端時出現錯誤: {ex.Message}");
        }
    }

    #endregion

    #region TCP 方法

    /// <summary>
    /// Adds or restarts a TCP listener for the specified port.
    /// </summary>
    /// <param name="portData">The port data for the listener.</param>
    private void AddTcpListener(PortData portData)
    {
        if (!tcpServerdatas.TryGetValue(portData.ProtocolName, out TCPServerData tcpServerData))
        {
            StartTcpListener(portData);
        }
        else
        {
            RestartTcpListener(tcpServerData);
        }
    }

    /// <summary>
    /// Starts a new TCP listener and adds it to the dictionary.
    /// </summary>
    /// <param name="portData">The port data for the listener.</param>
    private void StartTcpListener(PortData portData)
    {
        TcpListener tcpListener = null;
        tcpListener = new TcpListener(IPAddress.Any, int.Parse(portData.LocalPortDetails.Port));


        var tcpServerData = new TCPServerData
        {
            portData = portData,
            tcpListener = tcpListener,
            CancellationTokenSource = new CancellationTokenSource(),
        };
        tcpListener.Start();
        tcpServerdatas.TryAdd(portData.ProtocolName, tcpServerData);
        LogOnMainThread($"在端口 {portData.LocalPortDetails.Port} 上啟動了 TCP 伺服器端。");
        Task.Run(() => ListenForTcpClients(tcpServerData));
    }

    /// <summary>
    /// Restarts an existing TCP listener, resetting its cancellation token.
    /// </summary>
    /// <param name="tcpServerData">The TCP server data to restart.</param>
    private void RestartTcpListener(TCPServerData tcpServerData)
    {
        if (tcpServerData.CancellationTokenSource != null && !tcpServerData.CancellationTokenSource.IsCancellationRequested)
        {
            tcpServerData.CancellationTokenSource.Cancel();
            tcpServerData.CancellationTokenSource.Dispose();
        }

        tcpServerData.CancellationTokenSource = new CancellationTokenSource();
        tcpServerData.portData.IsConnected = true;

        UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
        Task.Run(() => ListenForTcpClients(tcpServerData));
    }

    /// <summary>
    /// Disposes and stops a TCP server listener, removing it from the dictionary.
    /// </summary>
    /// <param name="portData">The port data of the listener to dispose.</param>
    private void DisposeTcpServer(PortData portData)
    {
        if (tcpServerdatas.TryRemove(portData.LocalPortDetails.Port, out TCPServerData tcpServerData))
        {
            if (tcpServerData.CancellationTokenSource != null && !tcpServerData.CancellationTokenSource.IsCancellationRequested)
            {
                tcpServerData.CancellationTokenSource.Cancel();
                tcpServerData.CancellationTokenSource.Dispose();
            }

            if (tcpServerData.tcpListener != null)
            {
                tcpServerData.tcpListener.Stop();
                tcpServerData.tcpListener = null;
            }

            tcpServerData.Dispose();
        }
    }

    /// <summary>
    /// Disconnects a TCP server and updates the connection status for a single client.
    /// </summary>
    /// <param name="portData">The port data of the server to disconnect.</param>
    private void DisconnectTcpServer(PortData portData)
    {
        if (tcpServerdatas.TryGetValue(portData.ProtocolName, out TCPServerData tcpServerData))
        {
            tcpServerData.CancellationTokenSource.Cancel();
            tcpServerData.portData.IsConnected = false;

            UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
    }

    private async Task ListenForTcpClients(TCPServerData tcpServerData)
    {
        try
        {
            while (!tcpServerData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await tcpServerData.tcpListener.AcceptTcpClientAsync();
                    tcpServerData.portData.IsConnected = true;
                    LogOnMainThread($"新客戶端已連線: {client.Client.RemoteEndPoint}");
                    UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
                    _ = Task.Run(() => ReceiveTcpMessages(client, tcpServerData));
                }
                catch (SocketException ex)
                {
                    LogOnMainThread($"SocketException: {ex.Message}");
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogOnMainThread($"偵聽 TCP 客戶端時出現錯誤: {ex.Message}");
        }
        finally
        {
            LogOnMainThread($"TCP 伺服器。端口 {tcpServerData.portData.LocalPortDetails.Port} 已停止。");
        }
    }

    private readonly object maskTypeLock = new();

    private async Task ReceiveTcpMessages(TcpClient client, TCPServerData tcpServerData)
    {
        IPEndPoint remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
        NetworkStream stream = client.GetStream();
        var buffer = new byte[1024];
        var dataBuffer = new StringBuilder();  // 用於累積接收到的數據

        try
        {
            while (true)
            {
                tcpServerData.CancellationTokenSource.Token.ThrowIfCancellationRequested();

                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, tcpServerData.CancellationTokenSource.Token);
                if (bytesRead <= 0)
                {
                    LogDisconnection(remoteEndPoint);
                    break;
                }

                // 將收到的字節轉換為字符串，並寫入累積緩衝區
                string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                dataBuffer.Append(receivedData);

                // 嘗試提取完整的封包
                while (TryExtractCompletePacket(dataBuffer, out string completePacket))
                {
                    ProcessPacket(completePacket, tcpServerData);
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogOnMainThread($"接收訊息已被取消: {remoteEndPoint}");
        }
        catch (Exception ex)
        {
            LogOnMainThread($"接收來自 {remoteEndPoint} 的訊息時發生錯誤: {ex.Message}");
        }
        finally
        {
            client.Close();
            tcpServerData.portData.IsConnected = false;
            UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
        }
    }

    private bool TryExtractCompletePacket(StringBuilder dataBuffer, out string completePacket)
    {
        completePacket = null;

        // 查找資料中的 ID 部分
        int idStartIndex = dataBuffer.ToString().IndexOf("ID");
        if (idStartIndex == -1)
        {
            return false;  // 如果找不到 ID，返回 false
        }

        // 查找下一個資料包的開始（下一個 "ID"）
        int nextIdStartIndex = dataBuffer.ToString().IndexOf("ID", idStartIndex + 2);
        if (nextIdStartIndex == -1)
        {
            // 如果找不到下一個 ID，說明這是最後一個資料包
            // 直到緩衝區的結尾為止
            completePacket = dataBuffer.ToString(idStartIndex, dataBuffer.Length - idStartIndex);
            dataBuffer.Clear(); // 清除緩衝區，因為資料包已處理完
            return true;
        }
        else
        {
            // 如果找到下一個 ID，提取從當前 ID 到下一個 ID 之間的資料包
            completePacket = dataBuffer.ToString(idStartIndex, nextIdStartIndex - idStartIndex);

            // 移除已經處理過的部分，保留剩餘資料
            dataBuffer.Remove(0, nextIdStartIndex);

            return true;
        }
    }



    private void ProcessPacket(string packet, TCPServerData tcpServerData)
    {
        string currentMaskType;

        lock (maskTypeLock)
        {
            currentMaskType = tcpServerData.portData.MaskType;
        }

        switch (currentMaskType)
        {
            case "Robot to 10":
                HandleRobotTo10Message(tcpServerData, Encoding.UTF8.GetBytes(packet), packet.Length);
                break;
            case "Robot to 16":
                ProcessRobotTo16Message(packet, tcpServerData);
                break;
            case "original data":
                ProcessOriginalDataMessage(tcpServerData, packet, packet.Length);
                break;
            default:
                LogOnMainThread($"未識別的 MaskType: {currentMaskType}");
                break;
        }
    }


    private void LogDisconnection(IPEndPoint remoteEndPoint)
    {
        LogOnMainThread($"客戶端 {remoteEndPoint} 已斷開連接。");
    }

    private void HandleRobotTo10Message(TCPServerData tcpServerData, byte[] buffer, int bytesRead)
    {
        byte[] messageData = new byte[bytesRead];
        Array.Copy(buffer, 0, messageData, 0, bytesRead);

        if (messageData.Length >= 7 && messageData[0] == 0xDD && messageData[^1] == 0x77)
        {
            ProcessMessage(messageData);
        }
        else if (tcpServerData.portData == this.portData)
        {
            string hexData = BitConverter.ToString(messageData).Replace("-", " ");
            LogMonitorMainThread($"錯誤：收到的資料不以 0xDD 開頭或以 0x77 結尾。資料: {hexData}");
        }
    }

    private void ProcessOriginalDataMessage(TCPServerData tcpServerData, string data, int packetSize)
    {
        if (tcpServerData.portData == this.portData)
        {
            LogMonitorMainThread($"收到訊息，資料大小: {packetSize}，資料: {data}");
        }

        if (tcpClientdatas.TryGetValue(tcpServerData.portData.ProtocolName, out var tcpClientData) && tcpClientData.IsConnecting)
        {
            SendMessage(tcpServerData, data);
        }
    }


    private void ProcessRobotTo16Message(string data, TCPServerData tcpServerData)
    {
        if (string.IsNullOrWhiteSpace(data) || !data.Contains(":"))
        {
            LogMonitorMainThread("[錯誤]: 資料格式不正確或為空");
            return;
        }

        if (data.Contains("U�U") || data.Contains("::"))
        {
            LogMonitorMainThread("[錯誤]: 收到的資料包含無法正確分割的段落");
            return;
        }

        List<string> datas = data.Split(':').ToList();
        if (datas.Count < 3)
        {
            LogMonitorMainThread("[錯誤]: 收到的資料組數不符");
            return;
        }

        if (!int.TryParse(datas[0], out int format) ||
            !int.TryParse(datas[1], out int function) ||
            !int.TryParse(datas[2], out int length))
        {
            LogMonitorMainThread("[錯誤]: 格式、功能或長度欄位無法轉換為整數");
            return;
        }

        string sourceData = string.Empty;
        try
        {
            if (function == 1)
            {
                sourceData = $"{ConvertToIEEE754Hexadecimal(datas[3])}:{ConvertToIEEE754Hexadecimal(datas[4])}";
            }
            else if (function == 2)
            {
                sourceData = ConvertToIEEE754Hexadecimal(datas[3]);
            }
            else if (function is 3 or 7 or 8 or 10)
            {
                sourceData = "0x" + int.Parse(datas[3]).ToString("X2");
            }
        }
        catch (Exception ex)
        {
            LogMonitorMainThread($"[錯誤]: 生成源數據時發生錯誤 - {ex.Message}");
            return;
        }

        if ((length == 8 || length == 4) && string.IsNullOrEmpty(sourceData))
        {
            LogMonitorMainThread("[錯誤]: 長度與源數據不一致");
            return;
        }

        List<byte> bytesToCheck = new() { (byte)function, (byte)length };
        try
        {
            foreach (var hex in sourceData.Split(':'))
            {
                if (!string.IsNullOrEmpty(hex))
                {
                    bytesToCheck.Add(Convert.ToByte(hex.Trim()[2..], 16)); // Trim '0x'
                }
            }
        }
        catch (Exception ex)
        {
            LogMonitorMainThread($"[錯誤]: 構建校驗數據時發生錯誤 - {ex.Message}");
            return;
        }

        ushort calculatedCrc = CalculateCrc16(bytesToCheck.ToArray());
        int calculatedChecksum1 = calculatedCrc & 0xFF;
        int calculatedChecksum2 = (calculatedCrc >> 8) & 0xFF;

        string start = "0xDD";
        string stop = "0x77";
        string formatHex = "0x" + format.ToString("X2");
        string functionHex = "0x" + function.ToString("X2");
        string lengthHex = "0x" + length.ToString("X2");
        string checksum1Hex = "0x" + calculatedChecksum1.ToString("X2");
        string checksum2Hex = "0x" + calculatedChecksum2.ToString("X2");
        string checksourceData = function switch
        {
            1 or 2 => RemoveColons(sourceData),
            _ => sourceData
        };

        string source = length == 0
            ? $"{start}{formatHex}{functionHex}{lengthHex}{checksum1Hex}{checksum2Hex}{stop}"
            : $"{start}{formatHex}{functionHex}{lengthHex}{checksourceData}{checksum1Hex}{checksum2Hex}{stop}";

        List<byte> byteList = new();
        try
        {
            for (int i = 0; i < source.Length; i += 4)
            {
                if (i + 4 > source.Length) break;
                string hexValue = source.Substring(i + 2, 2);
                byteList.Add(Convert.ToByte(hexValue, 16));
            }
        }
        catch (Exception ex)
        {
            LogMonitorMainThread($"[錯誤]: 生成 ByteList 時發生錯誤 - {ex.Message}");
            return;
        }

        tcpServerData.sourceData = source;
        if (tcpClientdatas.TryGetValue(tcpServerData.portData.ProtocolName, out var tcpClientData) && tcpClientData.IsConnecting)
        {
            SendMessage(tcpServerData, byteList.ToArray());
        }
    }



    private string ConvertToIEEE754Hexadecimal(string decimalString)
    {
        if (!float.TryParse(decimalString, out float decimalNumber))
        {
            throw new ArgumentException("Invalid input format. Please provide a valid decimal number.");
        }

        byte[] bytes = BitConverter.GetBytes(decimalNumber);

        string ieeeHex = string.Join(":", bytes.Select(b => $"0x{b:X2}"));

        return ieeeHex;
    }

    private string RemoveColons(string input)
    {
        return input.Replace(":", string.Empty);
    }

    private TcpClient tcpClient;
    private NetworkStream networkStream;

    private void ProcessMessage(byte[] data)
    {
        byte start = data[0];
        byte format = data[1];
        byte function = data[2];
        byte state = data[3];
        byte length = data[4];
        byte stop = data[^1];

        float[] sourceBack = new float[length / 4];
        byte[] sourceData = data[5..(5 + length)];

        switch (function)
        {
            case 4:
                length = 0;
                break;

            case 5:
                if (length == 4)
                {
                    sourceBack[0] = BitConverter.ToSingle(sourceData, 0);
                }
                break;

            case 6:
                for (int i = 0; i < sourceData.Length; i += 4)
                {
                    sourceBack[i / 4] = BitConverter.ToSingle(sourceData, i);
                }
                break;
            default:
                break;
        }

        string message = function switch
        {
            4 => $"{function}:{state}:{length}",
            5 => $"{function}:{state}:{length}:{sourceBack[0]}",
            6 => $"{function}:{state}:{length}:{string.Join(":", sourceBack)}",
            _ => $"{function}:{state}:{length}"

        };
        string targetIp = "192.168.1.3";
        int targetPort = 12000;
        SendToTargetIp(targetIp, targetPort, message);
    }

    private async void SendToTargetIp(string ip, int port, string message)
    {
        try
        {
            if (tcpClient == null || !tcpClient.Connected)
            {
                tcpClient = new TcpClient(ip, port);
                networkStream = tcpClient.GetStream();
            }

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            await networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending message: {e.Message}");
            CloseTcpClient();
        }
    }

    private void CloseTcpClient()
    {
        if (tcpClient != null && tcpClient.Connected)
        {
            try
            {
                networkStream?.Close();
                tcpClient?.Close();
                tcpClient = null;
                networkStream = null;
                Debug.Log("TCP client closed.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error closing TCP client: {e.Message}");
            }
        }
    }

    /// <summary>
    /// CRC-16 驗證
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private ushort CalculateCrc16(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return crc;
    }

    /// <summary>
    /// 發送訊息到指定 IP、Port
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="tcpServerData"></param>
    private async void SendMessage(TCPServerData tcpServerData, string message)
    {
        if (!tcpClientdatas.ContainsKey(tcpServerData.portData.ProtocolName))
        {
            Debug.Log("沒有可用的 TCP 客戶端!");
            return;
        }

        var tcpClinetData = tcpClientdatas[tcpServerData.portData.ProtocolName];

        if (tcpClinetData.IsConnecting)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);

                NetworkStream stream = tcpClinetData.tcpClient.GetStream();
                await stream.WriteAsync(buffer, 0, buffer.Length);

                int packetSize = buffer.Length;
                tcpClinetData.portData.IsConnected = true;

                if (tcpClinetData.portData == this.portData)
                {
                    string messageTmp = $"傳送訊息，資料大小: {packetSize}，資料: {message}";
                    LogMonitorMainThread(messageTmp);
                }

            }
            catch (Exception ex)
            {
                LogOnMainThread($"發送訊息到 TCP 客戶端時出現錯誤: {ex.Message}", isError: true);
            }
        }
        else
        {
            tcpClinetData.portData.IsConnected = false;
            LogOnMainThread($"來自 {tcpClinetData.tcpClient.Client.RemoteEndPoint} TCP 客戶端已斷開連接: ", isError: true);
            UnityMainThreadDispatcher.Instance().Enqueue(() => tcpClinetData.portData.OnUpdate?.Invoke(tcpClinetData.portData));
        }
    }

    private async void SendMessage(TCPServerData tcpServerData, byte[] message)
    {
        if (!tcpClientdatas.TryGetValue(tcpServerData.portData.ProtocolName, out var tcpClientData))
        {
            LogOnMainThread("沒有可用的 TCP 客戶端!", isError: true);
            return;
        }

        if (!tcpClientData.IsConnecting || tcpClientData.tcpClient == null || !tcpClientData.tcpClient.Connected)
        {
            tcpClientData.portData.IsConnected = false;
            LogOnMainThread("TCP 客戶端已斷開連接", isError: true);
            return;
        }

        var stream = tcpClientData.tcpClient.GetStream();
        LogMonitorMainThread($"Request: {BitConverter.ToString(message).Replace("-", " ")}");

        await stream.WriteAsync(message, 0, message.Length);

        tcpClientData.portData.IsConnected = true;
        var ackbuffer = new byte[1024];
        using var ctsAck = new CancellationTokenSource(200);
        int ackBytesRead = await stream.ReadAsync(ackbuffer, 0, ackbuffer.Length, ctsAck.Token);
        if (ackBytesRead > 0)
        {
            string ackMessage = BitConverter.ToString(ackbuffer, 0, ackBytesRead).Replace("-", " ");
            LogMonitorMainThread($"ACK: {ackMessage}");
        }

        var responsebuffer = new byte[1024];
        using var ctsResponse = new CancellationTokenSource(400);
        int responseBytesRead = await stream.ReadAsync(responsebuffer, 0, responsebuffer.Length, ctsResponse.Token);
        if (responseBytesRead > 0)
        {
            string responseMessage = BitConverter.ToString(responsebuffer, 0, responseBytesRead).Replace("-", " ");
            LogMonitorMainThread($"Response: {responseMessage}");
            ProcessMessage(responsebuffer);
        }
    }

    #endregion

    #region UDP 方法

    /// <summary>
    /// 接收 UDP 訊息
    /// </summary>
    /// <param name="udpData">UDP 資料</param>
    /// <param name="port">端口</param>
    private async Task ReceiveUdpMessages(PortData portData, UdpData udpData)
    {
        using var sendClient = new UdpClient();
        IPEndPoint sendEndPoint = string.IsNullOrWhiteSpace(portData.TargetIP)
            ? new IPEndPoint(IPAddress.Broadcast, int.Parse(portData.LocalPortDetails.Port))
            : new IPEndPoint(IPAddress.Parse(portData.TargetIP), int.Parse(portData.LocalPortDetails.Port));

        if (string.IsNullOrWhiteSpace(portData.TargetIP))
            sendClient.EnableBroadcast = true;

        byte[] buffer = new byte[1024];  // 固定大小的緩衝區

        try
        {
            while (!udpData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await udpData.udpClient.ReceiveAsync().WithCancellation(udpData.CancellationTokenSource.Token);
                    int messageLength = result.Buffer.Length;

                    // 確認訊息是否超過緩衝區大小
                    if (messageLength > buffer.Length)
                    {
                        buffer = new byte[messageLength];  // 創建更大的緩衝區
                    }

                    Array.Copy(result.Buffer, buffer, messageLength);
                    string message = Encoding.UTF8.GetString(buffer, 0, messageLength);

                    portData.COMReceived += messageLength;
                    udpData.SourceData = message;

                    LogMonitorMainThread($"名稱: {portData.ProtocolName}。收到來自: {result.RemoteEndPoint} 的訊息，大小: {messageLength} bytes, 訊息: {message}");

                    await sendClient.SendAsync(result.Buffer, messageLength, sendEndPoint);
                    portData.NetReceived += messageLength;

                    LogMonitorMainThread($"名稱: {portData.ProtocolName}。傳送到: {sendEndPoint}, 大小: {messageLength} bytes, 訊息: {message}");
                    UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    Debug.Log($"UDP 客戶端已關閉: {ex.Message}");
                }
                catch (OperationCanceledException)
                {
                    Debug.Log("UDP 接收操作被取消。");
                }
                catch (Exception ex)
                {
                    Debug.Log($"接收 UDP 訊息時發生錯誤: {ex.Message}");
                }
            }
        }
        finally
        {
            Debug.Log("結束接收。");
        }

        udpData.Dispose();
        Debug.Log($"UDP 接收器已在端口 {portData.RemotePortDetails.Port} 上關閉。");
    }



    #endregion

    #region 獲取資料來源
    /// <summary>
    /// 通過你要監聽哪個port來獲取原始資料
    /// 參數說明：port為要監聽的port，length資料長度，connectionType選擇TCP、UDP
    /// </summary>
    /// <param name="port"></param>
    /// <param name="length"></param>
    /// <param name="connectionType"></param>
    /// <returns></returns>
    public string GetSourceData(string port, int length, ConnectionType connectionType)
    {
        if (length <= 0)
        {
            Debug.LogWarning("Invalid length parameter. Length must be greater than 0.");
            return string.Empty;
        }

        string sourceData = string.Empty;

        if (connectionType == ConnectionType.UDP && udpClients.TryGetValue(port, out UdpData udpData))
        {
            lock (udpData)
            {
                sourceData = udpData.SourceData;
                udpData.SourceData = string.Empty;
            }
        }
        else if (connectionType == ConnectionType.TCP && tcpServerdatas.TryGetValue(port, out TCPServerData tcpData))
        {
            lock (tcpData)
            {
                sourceData = tcpData.sourceData;
                tcpData.sourceData = string.Empty;
            }
        }

        if (!string.IsNullOrEmpty(sourceData))
        {
            return sourceData.Length > length ? sourceData[..length] : sourceData;
        }

        return string.Empty;
    }
    #endregion

    #region 反初始化
    /// <summary>
    /// 當應用程序退出時關閉客戶端
    /// </summary>
    private async Task ShutdownClientsAsync()
    {
        var udpDisposalTasks = new List<Task>();
        var tcpServerDisposalTasks = new List<Task>();
        var tcpClientDisposalTasks = new List<Task>();

        foreach (var udpData in udpClients.Values)
        {
            udpDisposalTasks.Add(Task.Run(() => udpData.Dispose()));
        }

        foreach (var tcpServerData in tcpServerdatas.Values)
        {
            tcpServerDisposalTasks.Add(Task.Run(() => tcpServerData.Dispose()));
        }

        foreach (var tcpClientData in tcpClientdatas.Values)
        {
            tcpClientDisposalTasks.Add(Task.Run(() => tcpClientData.Dispose()));
        }

        await Task.WhenAll(udpDisposalTasks);
        await Task.WhenAll(tcpServerDisposalTasks);
        await Task.WhenAll(tcpClientDisposalTasks);

        udpClients.Clear();
        tcpServerdatas.Clear();
        tcpClientdatas.Clear();
    }
    #endregion

    #region 監控控制台

    public void MonitorConsole(PortData portData)
    {
        this.portData = portData;
    }

    private void LogMonitorMainThread(string message, bool isError = false)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (isError)
            {
                Debug.LogError(message);
            }
            else
            {
                Debug.Log(message);
            }

            if (monitorConsole != null)
            {
                monitorConsole.AddLog(message);
            }
            else
            {
                Debug.LogWarning("MonitorConsole is null, cannot add log.");
            }
        });
    }


    #endregion

    #region 日誌封裝
    private void LogOnMainThread(string message, bool isError = false)
    {
        if (message.Length > 1000)
        {
            message = message[..1000] + "... (truncated)";
        }

        string formattedMessage = FormatLogMessage(message, isError);

        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (isError)
            {
                Debug.LogError(formattedMessage);
            }
            else
            {
                Debug.Log(formattedMessage);
            }
            consoleUI.AddLog(formattedMessage);
        });
    }


    /// <summary>
    /// 格式化日誌訊息
    /// </summary>
    /// <param name="message">原始訊息</param>
    /// <param name="isError">是否為錯誤訊息</param>
    /// <returns>格式化後的訊息</returns>
    private string FormatLogMessage(string message, bool isError)
    {
        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string errorLabel = isError ? "[Error]" : "[Info]";
        return $"[{timeStamp}] {errorLabel} {message}";
    }

    #endregion

    #region 退出應用
    public async void DeInit()
    {
        await ShutdownClientsAsync();
    }

    #endregion
}


