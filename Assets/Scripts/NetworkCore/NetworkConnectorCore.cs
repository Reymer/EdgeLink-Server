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
using System.IO;
using static NetworkPortManager;
using System.Buffers;
using static NetworkConnectorCore;

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
            lock (lockObj)
            {
                if (disposed) return;
                disposed = true;

                CancellationTokenSource?.Cancel();
                CancellationTokenSource?.Dispose();

                udpClient?.Close();
                udpClient?.Dispose();
            }
        }
    }

    public class TCPServerData : IDisposable
    {
        public TcpListener tcpListener;
        public CancellationTokenSource cancellationTokenSource = new();
        public PortData portData;
        public string sourceData = string.Empty;
        public bool disposed = false;
        private readonly object lockObj = new();
        public void Dispose()
        {
            lock (lockObj)
            {
                if (disposed) return;
                disposed = true;                   
                cancellationTokenSource?.Cancel();
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                tcpListener?.Server?.Dispose();
            }
        }
    }

    public class TCPClientData : IDisposable
    {
        public TcpClient tcpClient;
        public CancellationTokenSource CancellationTokenSource = new();
        private bool disposed = false;
        private readonly object lockObj = new();
        public string remotePort = string.Empty;
        public string localPort = string.Empty;
        public string remoteIP = string.Empty;
        public string netProtocol = string.Empty;
        public PortData portData;
        public bool IsConnecting;
        public void Dispose()
        {
            lock (lockObj)
            {
                if (disposed) return;
                disposed = true;

                CancellationTokenSource?.Cancel();
                CancellationTokenSource?.Dispose();
                CancellationTokenSource = null;

                tcpClient?.Close();
                tcpClient?.Dispose();
                tcpClient = null;
            }
        }
    }
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
        if (!udpClients.ContainsKey(portData.RemotePortDetails.Port))
        {
            LogOnMainThread($"未找到 UDP 伺服器，端口 {portData.RemotePortDetails.Port}");
            return;
        }

        var udpData = udpClients[portData.RemotePortDetails.Port];

        if (portData.IsConnected && udpData.udpClient != null)
        {
            try
            {
                udpData.CancellationTokenSource.Cancel();
                udpData.IsConnecting = false;
                portData.IsConnected = false;

                await Task.Delay(100);

                udpData.udpClient.Dispose();
                udpData.Dispose();
                udpData.udpClient = null;

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
        if (!tcpClientdatas.ContainsKey(portData.RemotePortDetails.Port))
        {
            LogOnMainThread($"未找到 TCP 客户端，端口 {portData.RemotePortDetails.Port}");
            return;
        }
        else
        {
            var tcpClientData = tcpClientdatas[portData.RemotePortDetails.Port];
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
        if (tcpClientdatas.TryGetValue(portData.RemotePortDetails.Port, out var tcpClientData))
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
            tcpClientdatas.TryAdd(portData.RemotePortDetails.Port, tcpClientData);
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
        if (udpClients.TryGetValue(portData.RemotePortDetails.Port, out var existingServerData))
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
            // 綁定到特定的端口來接收數據
            var udpClient = new UdpClient(int.Parse(portData.RemotePortDetails.Port));
            portData.IsConnected = true;
            existingServerData = new UdpData
            {
                portData = portData,
                udpClient = udpClient,
                CancellationTokenSource = new CancellationTokenSource(),
            };

            udpClients[portData.RemotePortDetails.Port] = existingServerData;

            LogOnMainThread($"在端口 {portData.RemotePortDetails.Port} 上啟動了 UDP 伺服器端。");

            // 開始接收 UDP 數據
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

    private void AddTcpListener(PortData portData)
    {
        if (!tcpServerdatas.ContainsKey(portData.LocalPortDetails.Port))
        {
            StartTcpListener(portData);
        }
        else
        {
            if (tcpServerdatas.TryGetValue(portData.LocalPortDetails.Port, out TCPServerData tcpServerData))
            {
                tcpServerData.cancellationTokenSource.Cancel();
                tcpServerData.cancellationTokenSource.Dispose();
                tcpServerData.cancellationTokenSource = new CancellationTokenSource(); 
                tcpServerData.portData.IsConnected = true; 
                UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
                Task.Run(() => ListenForTcpClients(tcpServerData));
            }
        }
    }

    private void StartTcpListener(PortData portData)
    {
        var tcpListener = new TcpListener(IPAddress.Any, int.Parse(portData.LocalPortDetails.Port));
        var tcpServerData = new TCPServerData
        {
            portData = portData,
            tcpListener = tcpListener,
            cancellationTokenSource = new CancellationTokenSource(),
        };
        tcpListener.Start();
        tcpServerdatas.TryAdd(portData.LocalPortDetails.Port, tcpServerData);
        LogOnMainThread($"在端口 {portData.LocalPortDetails.Port} 上啟動了 TCP 伺服器端。");
        Task.Run(() => ListenForTcpClients(tcpServerData));
    }

    private void DisposeTcpServer(PortData portData)
    {
        if (tcpServerdatas.TryRemove(portData.LocalPortDetails.Port, out TCPServerData tcpServerData))
        {
            tcpServerData.cancellationTokenSource.Cancel();
            tcpServerData.cancellationTokenSource.Dispose();
            tcpServerData.tcpListener.Stop();
            tcpServerData.tcpListener = null;
            tcpServerData.Dispose();
        }
    }

    /// <summary>
    /// 斷線 TCP Server，處理單一客戶端斷開
    /// </summary>
    /// <param name="portData"></param>
    private void DisconnectTcpServer(PortData portData)
    {
        if (tcpServerdatas.TryGetValue(portData.LocalPortDetails.Port, out TCPServerData tcpServerData))
        {
            tcpServerData.cancellationTokenSource.Cancel();
            tcpServerData.portData.IsConnected = false;
            UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
    }

    private async Task ListenForTcpClients(TCPServerData tcpServerData)
    {
        try
        {
            while (!tcpServerData.cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                     var client = await tcpServerData.tcpListener.AcceptTcpClientAsync();
                    tcpServerData.portData.IsConnected = true;
                    LogOnMainThread($"新客戶端已連接: {client.Client.RemoteEndPoint}");
                    UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
                    _ = Task.Run(() => ReceiveTcpMessages(client, tcpServerData));
                }
                catch (SocketException ex)
                {
                    LogOnMainThread($"SocketException: {ex.Message}");
                }
            }
        }
        catch (ObjectDisposedException)
        {
            LogOnMainThread("TCP Listener has been stopped.");
        }
        catch (Exception ex)
        {
            LogOnMainThread($"Error in listening for TCP clients: {ex.Message}");
        }
    }

    private async Task ReceiveTcpMessages(TcpClient client, TCPServerData tcpServerData)
    {
        try
        {
            IPEndPoint remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            var buffer = new byte[4096];
            using NetworkStream stream = client.GetStream();

            while (!tcpServerData.cancellationTokenSource.Token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, tcpServerData.cancellationTokenSource.Token);

                if (bytesRead > 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string outputMessage;
                    string debugPrefix;

                    if (message.StartsWith("0x") && tcpServerData.portData.MaskType == "16 to 10")
                    {
                        int decimalValue = Convert.ToInt32(message, 16);
                        outputMessage = decimalValue.ToString();
                        debugPrefix = "[十進制]"; // 標記轉換過的消息
                    }
                    else
                    {
                        outputMessage = message;
                        debugPrefix = "[原始]"; // 標記為原始消息
                    }

                    tcpServerData.sourceData = outputMessage;
                    int packetSize = bytesRead;

                    if (tcpServerData.portData == this.portData)
                    {
                        LogMonitorMainThread($"TCP 伺服器。收到消息來自: {remoteEndPoint}, 原始資料大小: {packetSize}, 進制: {debugPrefix} 消息: {outputMessage}");
                    }

                    if (tcpClientdatas.ContainsKey(tcpServerData.portData.LocalPortDetails.Port))
                    {
                        var tcpClinetData = tcpClientdatas[tcpServerData.portData.LocalPortDetails.Port];
                        if (tcpClinetData.IsConnecting)
                        {
                            SendMessage(tcpServerData);
                        }
                    }
                }
                else
                {
                    LogOnMainThread($"客戶端 {remoteEndPoint} 已斷開連接。");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogOnMainThread($"Error receiving messages from {client.Client.RemoteEndPoint}: {ex.Message}");
        }
        finally
        {
            client.Close();
            tcpServerData.portData.IsConnected = false;
            UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
        }
    }


    /// <summary>
    /// 發送訊息到指定 IP、Port
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="tcpServerData"></param>
    private void SendMessage(TCPServerData tcpServerData)
    {
        if (!tcpClientdatas.ContainsKey(tcpServerData.portData.LocalPortDetails.Port))
        {
            Debug.Log($"沒有可用的 TCP 客戶端!");
            return;
        }

        var tcpClinetData = tcpClientdatas[tcpServerData.portData.LocalPortDetails.Port];

        if (tcpClinetData.IsConnecting)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                var buffer = Encoding.UTF8.GetBytes(tcpServerData.sourceData);
                memoryStream.Write(buffer, 0, buffer.Length);

                NetworkStream stream = tcpClinetData.tcpClient.GetStream();
                memoryStream.Position = 0;
                memoryStream.CopyTo(stream);
                int packetSize = buffer.Length;
                tcpClinetData.portData.IsConnected = true;
                if(tcpClinetData.portData == this.portData)
                {
                    LogMonitorMainThread($"TCP 客戶端。傳送訊息到達: {tcpClinetData.tcpClient.Client.RemoteEndPoint}, 封包大小: {packetSize} bytes, 訊息: {tcpServerData.sourceData}");
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


    #endregion

    #region UDP 方法

    /// <summary>
    /// 接收 UDP 訊息
    /// </summary>
    /// <param name="udpData">UDP 資料</param>
    /// <param name="port">端口</param>
    private async Task ReceiveUdpMessages(PortData portData, UdpData udpData)
    {
        // 創建一個 UDP 用來發送廣播訊息
        using (var sendClient = new UdpClient())
        {
            sendClient.EnableBroadcast = true; // 啟用廣播
            IPEndPoint sendEndPoint = new(IPAddress.Broadcast, int.Parse(portData.LocalPortDetails.Port));

            long totalReceivedBytes = 0;
            long totalSentBytes = 0;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024); // 16 KB 的緩衝區

            try
            {
                while (!udpData.CancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        // 接收 UDP 數據
                        var result = await udpData.udpClient.ReceiveAsync().WithCancellation(udpData.CancellationTokenSource.Token);

                        int messageLength = result.Buffer.Length;

                        // 動態調整緩衝區大小
                        if (messageLength > buffer.Length)
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                            buffer = ArrayPool<byte>.Shared.Rent(messageLength);
                        }

                        // 複製收到的數據到緩衝區
                        Array.Copy(result.Buffer, buffer, messageLength);

                        // 轉換收到的數據為字串
                        string message = Encoding.UTF8.GetString(buffer, 0, messageLength);
                        portData.COMReceived += messageLength;
                        totalReceivedBytes += messageLength;

                        udpData.SourceData = message;

                        // 如果是對應的 portData，記錄日誌
                        if (udpData.portData == this.portData)
                        {
                            LogMonitorMainThread($"UDP 伺服器。收到來自: {result.RemoteEndPoint} 的訊息，封包大小: {messageLength} bytes, 訊息: {message}");
                        }

                        // 將接收到的訊息廣播回去
                        await sendClient.SendAsync(result.Buffer, messageLength, sendEndPoint);

                        portData.NetReceived += messageLength;
                        totalSentBytes += messageLength;

                        // 日誌廣播訊息
                        if (udpData.portData == this.portData)
                        {
                            LogMonitorMainThread($"UDP 客戶端。傳送訊息到: {sendEndPoint}, 封包大小: {messageLength} bytes, 訊息: {message}");
                        }

                        // 在主線程上調用更新函數
                        UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                    {
                        Debug.Log($"端口 {portData.RemotePortDetails.Port} 上的 UDP 客戶端已關閉。 {ex.Message}");
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log($"端口 {portData.RemotePortDetails.Port} 上的 UDP 接收操作被取消。");
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"接收 UDP 訊息時發生錯誤: {ex.Message}");
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer); // 釋放緩衝區
            }
        }

        udpData.Dispose(); // 釋放 udpData 資源
        Debug.Log($"端口 {portData.RemotePortDetails.Port} 上的 UDP 接收器已經關閉。");
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

        foreach(var tcpClientData in tcpClientdatas.Values)
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

    #region MonitorConsole
    public void MonitorConsole(PortData portData)
    {
        this.portData = portData;
    }

    private void LogMonitorMainThread(string message, bool isError = false)
    {
        string formattedMessage = FormatLogMessage(message, isError);

        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            // 輸出錯誤或日誌訊息到 Unity Console
            if (isError)
            {
                Debug.LogError(formattedMessage);
            }
            else
            {
                Debug.Log(formattedMessage);
            }

            // 檢查 monitorConsole 是否可用，然後添加日誌
            if (monitorConsole != null)
            {
                monitorConsole.AddLog(formattedMessage);
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


