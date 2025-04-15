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

    /// <summary>
    /// TCP 伺服器資料
    /// </summary>
    public class TCPServerData : IDisposable
    {
        public TcpListener tcpListener;
        public CancellationTokenSource CancellationTokenSource = new();
        public AsyncMessageQueue<byte[]> asyncMessageQueue = new();
        public IPEndPoint RemoteEndPoint;
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

    /// <summary>
    /// TCP 客戶端資料
    /// </summary>
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


    #region 釋放資源
    /// <summary>
    /// 釋放資源
    /// </summary>
    /// <param name="disposed"></param>
    /// <param name="lockObj"></param>
    /// <param name="disposeAction"></param>

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

    #region 初始化
    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="consoleUI"></param>
    /// <param name="monitorConsole"></param>
    public void Init(ConsoleUI consoleUI, MonitorConsole monitorConsole)
    {
        this.consoleUI = consoleUI;
        this.monitorConsole = monitorConsole;
    }
    #endregion
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

        if(UnityMainThreadDispatcher.Instance() != null)
        {
            UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
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
    /// 重新啟動 TCP 監聽器。
    /// </summary>
    /// <param name="tcpServerData"></param>

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
    /// 釋放 TCP 伺服器。
    /// </summary>
    /// <param name="portData"></param>
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
    /// 斷開 TCP 伺服器。
    /// </summary>
    /// <param name="portData"></param>
    private void DisconnectTcpServer(PortData portData)
    {
        if (tcpServerdatas.TryGetValue(portData.ProtocolName, out TCPServerData tcpServerData))
        {
            tcpServerData.CancellationTokenSource.Cancel();
            tcpServerData.portData.IsConnected = false;

            UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
    }

    /// <summary>
    /// 監聽 TCP 客戶端。
    /// </summary>
    /// <param name="tcpServerData"></param>
    /// <returns></returns>
    private async Task ListenForTcpClients(TCPServerData tcpServerData)
    {
        try
        {
            while (!tcpServerData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await tcpServerData.tcpListener.AcceptTcpClientAsync();
                    tcpServerData.RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                    tcpServerData.portData.IsConnected = true;

                    LogOnMainThread($"新客戶端已連線: {tcpServerData.RemoteEndPoint}");
                    SendClientEventMessage(tcpServerData, "CONNECTED");

                    UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));

                    _ = Task.Run(() => ReceiveTcpMessages(client, tcpServerData));
                    _ = Task.Run(() => ProcessTcpPackets(tcpServerData));
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

    /// <summary>
    /// 接收來自 TCP 客戶端的訊息。
    /// </summary>
    /// <param name="client"></param>
    /// <param name="tcpServerData"></param>
    /// <returns></returns>
    private async Task ReceiveTcpMessages(TcpClient client, TCPServerData tcpServerData)
    {
        tcpServerData.RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
        NetworkStream stream = client.GetStream();
        var buffer = new byte[1024];

        try
        {
            while (!tcpServerData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, tcpServerData.CancellationTokenSource.Token);
                if (bytesRead <= 0)
                {
                    LogOnMainThread($"客戶端 {tcpServerData.RemoteEndPoint} 已斷開連接。");
                    break;
                }

                byte[] packet = new byte[bytesRead];
                Array.Copy(buffer, 0, packet, 0, bytesRead);
                tcpServerData.asyncMessageQueue.Enqueue(packet);
            }
        }
        catch (OperationCanceledException)
        {
            LogOnMainThread($"接收訊息已被取消: {tcpServerData.RemoteEndPoint}");
        }
        catch (Exception ex)
        {
            LogOnMainThread($"接收來自 {tcpServerData.RemoteEndPoint} 的訊息時發生錯誤: {ex.Message}");
        }
        finally
        {
            client.Close();
            tcpServerData.portData.IsConnected = false;
            SendClientEventMessage(tcpServerData, "DISCONNECTED", "Remote closed or error");
            UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
        }
    }

    /// <summary>
    /// 處理 TCP 封包。
    /// </summary>
    /// <param name="tcpServerData"></param>
    /// <returns></returns>
    private async Task ProcessTcpPackets(TCPServerData tcpServerData)
    {
        var dataBuffer = new StringBuilder();
        var token = tcpServerData.CancellationTokenSource.Token;
        string source = tcpServerData.RemoteEndPoint?.ToString() ?? "Unknown";

        try
        {
            while (!token.IsCancellationRequested)
            {
                byte[] packet = await tcpServerData.asyncMessageQueue.DequeueAsync(token);
                string receivedData = Encoding.UTF8.GetString(packet);
                dataBuffer.Append(receivedData);

                string bufferString = dataBuffer.ToString();
                string[] packets = bufferString.Split('\n');

                // 最後一個可能是不完整的封包，不處理，保留回去
                for (int i = 0; i < packets.Length - 1; i++)
                {
                    string completePacket = packets[i].Trim(); // 去掉空白與 \r

                    if (string.IsNullOrWhiteSpace(completePacket))
                        continue;

                    string currentMaskType = tcpServerData.portData.MaskType;

                    try
                    {
                        switch (currentMaskType)
                        {
                            case "Robot to 10":
                                var packetBytes = Encoding.UTF8.GetBytes(completePacket);
                                HandleRobotTo10Message(tcpServerData, packetBytes, packetBytes.Length);
                                break;

                            case "Robot to 16":
                                ProcessRobotTo16Message(completePacket, tcpServerData);
                                break;

                            case "original data":
                                var originalBytes = Encoding.UTF8.GetBytes(completePacket);
                                HandleOriginalDataMessage(tcpServerData, completePacket, originalBytes.Length);
                                break;

                            default:
                                LogOnMainThread($"[{source}] 未識別的 MaskType: {currentMaskType}");
                                break;
                        }
                    }
                    catch (Exception innerEx)
                    {
                        LogOnMainThread($"[{source}] 處理封包失敗: {completePacket}，錯誤: {innerEx.Message}", isError: true);
                    }
                }

                // 將最後一個未結束的片段保留下來
                dataBuffer.Clear();
                if (!bufferString.EndsWith("\n"))
                {
                    dataBuffer.Append(packets[^1]); // C# 8.0 以後可用 ^1 代表最後一個元素
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogOnMainThread($"[{source}] 封包處理取消");
        }
        catch (Exception ex)
        {
            LogOnMainThread($"[{source}] 封包處理錯誤: {ex.Message}", isError: true);
        }
    }



    /// <summary>
    /// 嘗試提取完整的封包。
    /// </summary>
    /// <param name="dataBuffer"></param>
    /// <param name="completePacket"></param>
    /// <returns></returns>

    private bool TryExtractCompletePacket(StringBuilder dataBuffer, out string completePacket)
    {
        completePacket = null;
        string bufferStr = dataBuffer.ToString();

        int newlineIndex = bufferStr.IndexOf('\n');
        if (newlineIndex == -1)
            return false;

        completePacket = bufferStr.Substring(0, newlineIndex).TrimEnd('\r');
        dataBuffer.Remove(0, newlineIndex + 1); // ✅ 這行很重要

        return true;
    }




    /// <summary>
    /// 發送事件消息到 TCP 客戶端。
    /// </summary>
    /// <param name="serverData"></param>
    /// <param name="eventType"></param>
    /// <param name="reason"></param>
    private void SendClientEventMessage(TCPServerData serverData, string eventType, string reason = null)
    {
        if (tcpClientdatas.TryGetValue(serverData.portData.ProtocolName, out var clientData) &&
            clientData.IsConnecting &&
            clientData.tcpClient?.Connected == true)
        {
            var stream = clientData.tcpClient.GetStream();

            byte[] buffer = Encoding.UTF8.GetBytes(eventType + "\n");
            stream.Write(buffer, 0, buffer.Length);

            LogMonitorMainThread($"主動通知 Client: {eventType}");
        }
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

    private void HandleOriginalDataMessage(TCPServerData tcpServerData, string data, int packetSize)
    {
        if (tcpServerData.portData == this.portData)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogMonitorMainThread($"[{timestamp}] 收到訊息，資料大小: {packetSize}，資料: {data}");
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
                if (length == 5)
                {
                    sourceBack[0] = BitConverter.ToSingle(sourceData, 1);
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
            5 => $"{function}:{state}:{length}:{sourceData[0]}:{sourceBack[0]}",
            6 => $"{function}:{state}:{length}:{string.Join(":", sourceBack)}",
            9 => $"{function}:{state}:{length}:{sourceData[0]}",
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
                await stream.WriteAsync(buffer, 0, buffer.Length); // 使用異步寫入

                int packetSize = buffer.Length;
                tcpClinetData.portData.IsConnected = true;

                if (tcpClinetData.portData == this.portData)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    string messageTmp = $"[ {timestamp} ] 收到訊息，資料大小: : {packetSize}，資料: {message}";
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



        var buffer = new byte[1024];
        try
        {
            while (tcpClientData.tcpClient.Connected) // 持續監聽直到斷線
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    ProcessReceivedData(buffer, bytesRead);
                }
            }
        }
        catch (Exception ex)
        {
            LogOnMainThread($"監聽期間出錯: {ex.Message}", isError: true);
        }

        //var ackbuffer = new byte[1024];
        //using var ctsAck = new CancellationTokenSource(200);
        //int ackBytesRead = await stream.ReadAsync(ackbuffer, 0, ackbuffer.Length, ctsAck.Token);
        //if (ackBytesRead > 0)
        //{
        //    string ackMessage = BitConverter.ToString(ackbuffer, 0, ackBytesRead).Replace("-", " ");
        //    LogMonitorMainThread($"ACK: {ackMessage}");
        //}

        //var responsebuffer = new byte[1024];
        //using var ctsResponse = new CancellationTokenSource(400);
        //int responseBytesRead = await stream.ReadAsync(responsebuffer, 0, responsebuffer.Length, ctsResponse.Token);
        //if (responseBytesRead > 0)
        //{
        //    string responseMessage = BitConverter.ToString(responsebuffer, 0, responseBytesRead).Replace("-", " ");
        //    LogMonitorMainThread($"Response: {responseMessage}");
        //    ProcessMessage(responsebuffer);
        //}
    }
    private void ProcessReceivedData(byte[] buffer, int bytesRead)
    {
        int currentIndex = 0;
        while (currentIndex < bytesRead)
        {
            // 找到頭標記
            int startIndex = Array.IndexOf(buffer, (byte)0xDD, currentIndex);
            if (startIndex == -1 || startIndex >= bytesRead)
                break;

            // 找到尾標記
            int endIndex = Array.IndexOf(buffer, (byte)0x77, startIndex);
            if (endIndex == -1 || endIndex >= bytesRead)
                break;

            // 確定封包長度並提取資料
            int packetLength = endIndex - startIndex + 1;
            if (packetLength <= 2) // 無效封包 (至少需要包含類型位元)
            {
                currentIndex = endIndex + 1;
                continue;
            }

            byte[] packet = new byte[packetLength];
            Array.Copy(buffer, startIndex, packet, 0, packetLength);

            // 根據協議解析封包
            byte type = packet[1]; // 第二個位元是類型
            string messageContent = BitConverter.ToString(packet).Replace("-", " ");

            if (type == 0x02) // ACK
            {
                LogMonitorMainThread($"ACK: {messageContent}");
            }
            else if (type == 0x03) // Response
            {
                LogMonitorMainThread($"Response: {messageContent}");
                ProcessMessage(packet);
            }
            else
            {
                LogOnMainThread($"未知的回應類型: {type}", isError: true);
            }

            // 更新當前索引，繼續解析下一個封包
            currentIndex = endIndex + 1;
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

    #region 異步訊息佇列
    /// <summary>
    /// 異步訊息佇列
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AsyncMessageQueue<T>
    {
        private readonly ConcurrentQueue<T> queue = new();
        private readonly SemaphoreSlim semaphoreSlim = new(0);

        /// <summary>
        /// 放入佇列
        /// </summary>
        /// <param name="item"></param>
        public void Enqueue(T item)
        {
            queue.Enqueue(item);
            semaphoreSlim.Release();
        }

        /// <summary>
        /// 取出佇列
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<T> DequeueAsync(CancellationToken cancellationToken = default)
        {
            await semaphoreSlim.WaitAsync(cancellationToken);
            queue.TryDequeue(out var item);
            return item;
        }
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
            if(consoleUI != null)
            {
                consoleUI.AddLog(formattedMessage);
            }
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


