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
using System.Linq;
using UnityEngine.UIElements;

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

                tcpListener?.Stop();
                tcpListener = null;

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
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
        int reconnectDelay = 10000; 

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
        int reconnectDelay = 10000;

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

    #region TCP Methods

    /// <summary>
    /// Adds or restarts a TCP listener for the specified port.
    /// </summary>
    /// <param name="portData">The port data for the listener.</param>
    private void AddTcpListener(PortData portData)
    {
        if (!tcpServerdatas.TryGetValue(portData.LocalPortDetails.Port, out TCPServerData tcpServerData))
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

    /// <summary>
    /// Restarts an existing TCP listener, resetting its cancellation token.
    /// </summary>
    /// <param name="tcpServerData">The TCP server data to restart.</param>
    private void RestartTcpListener(TCPServerData tcpServerData)
    {
        if (tcpServerData.cancellationTokenSource != null && !tcpServerData.cancellationTokenSource.IsCancellationRequested)
        {
            tcpServerData.cancellationTokenSource.Cancel();
            tcpServerData.cancellationTokenSource.Dispose();
        }

        tcpServerData.cancellationTokenSource = new CancellationTokenSource();
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
            if (tcpServerData.cancellationTokenSource != null && !tcpServerData.cancellationTokenSource.IsCancellationRequested)
            {
                tcpServerData.cancellationTokenSource.Cancel();
                tcpServerData.cancellationTokenSource.Dispose();
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
        if (tcpServerdatas.TryGetValue(portData.LocalPortDetails.Port, out TCPServerData tcpServerData))
        {
            tcpServerData.cancellationTokenSource.Cancel();
            tcpServerData.portData.IsConnected = false;

            UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
    }

    #endregion

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
        try
        {
            IPEndPoint remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            var buffer = new byte[1024];
            NetworkStream stream = client.GetStream();

            while (!tcpServerData.cancellationTokenSource.Token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, tcpServerData.cancellationTokenSource.Token);

                if (bytesRead > 0)
                {
                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string currentMaskType;

                    lock (maskTypeLock)
                    {
                        currentMaskType = tcpServerData.portData.MaskType;
                    }

                    if (currentMaskType.Equals("Robot to 10"))
                    {
                        List<string> datas = SplitDataIntoGroups(data, 4);
                        if (IsValidMessage(datas))
                        {
                            await ProcessMessage(tcpServerData, client, datas);
                        }
                        else
                        {
                            if (tcpServerData.portData == this.portData)
                            {
                                LogMonitorMainThread($"錯誤：收到的資料不以 0xDD 開頭或以 0x77 結尾。資料: {data}");
                            }
                        }
                    }
                    else if (currentMaskType.Equals("Robot to 16"))
                    {
                        ProcessRobotTo16Message(data, client, tcpServerData);
                    }
                    else if (currentMaskType.Equals("original data"))
                    {
                        int packetSize = bytesRead;
                        if (tcpServerData.portData == this.portData)
                        {
                            LogMonitorMainThread($"收到訊息，資料大小: {packetSize}，資料: {data}");
                        }

                        if (tcpClientdatas.TryGetValue(tcpServerData.portData.LocalPortDetails.Port, out var tcpClientData) && tcpClientData.IsConnecting)
                        {
                            SendMessage(tcpServerData, data);
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
            LogOnMainThread($"接收來自 {client.Client.RemoteEndPoint} 的訊息時發生錯誤: {ex.Message}");
        }
        finally
        {
            client.Close();
            tcpServerData.portData.IsConnected = false;
            UnityMainThreadDispatcher.Instance().Enqueue(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData));
        }
    }
    private bool IsValidMessage(List<string> datas)
    {
        return datas.Count >= 7 && datas[0] == "0xDD" && datas[^1] == "0x77";
    }

    private void ProcessRobotTo16Message(string data, TcpClient client, TCPServerData tcpServerData)
    {
        if (data.Contains("::"))
        {
            LogMonitorMainThread("[錯誤]: 收到的資料包含空白段落，無法正確分割");
            return;
        }
        List<string> datas = data.Split(':').ToList();

        if (datas.Count < 5 || !int.TryParse(datas[0], out _) || !int.TryParse(datas[1], out _) || !int.TryParse(datas[2], out _))
        {
            LogMonitorMainThread("[錯誤]: 收到的資料格式不正確 - 資料應包含格式、功能、長度、資料組和檢查碼");
            return;
        }

        int format = Convert.ToInt32(datas[0]);
        int function = Convert.ToInt32(datas[1]);
        int length = Convert.ToInt32(datas[2]);
        int dataGroupCount = length;

        if (datas.Count < 3 + dataGroupCount + 2)
        {
            LogMonitorMainThread("[錯誤]: 收到的資料組數不符，缺少資料或檢查碼");
            return;
        }

        string sourceData = string.Join(":", datas.Skip(3).Take(dataGroupCount).Select(d => $"{Convert.ToInt32(d)}"));
        int checksumIndex = 3 + dataGroupCount;
        if (!int.TryParse(datas[checksumIndex], out int checksum1) || !int.TryParse(datas[checksumIndex + 1], out int checksum2))
        {
            LogMonitorMainThread("[錯誤]: 檢查碼格式不正確");
            return;
        }
        List<byte> bytesToCheck = new() { (byte)function, (byte)length };
        bytesToCheck.AddRange(datas.Skip(3).Take(dataGroupCount).Select(d => Convert.ToByte(d)));
        ushort calculatedCrc = CalculateCrc16(bytesToCheck.ToArray());
        int calculatedChecksum1 = calculatedCrc & 0xFF;
        int calculatedChecksum2 = (calculatedCrc >> 8) & 0xFF;
        bool isValidChecksum = (calculatedChecksum1 == checksum1) && (calculatedChecksum2 == checksum2);
        if (!isValidChecksum)
        {
            LogMonitorMainThread("[錯誤]: 檢查碼驗證失敗，收到的檢查碼與計算結果不符");
        }
        string state = isValidChecksum ? "1" : "2";
        string ackMessage = $"[ACK]:{format}:{function}:{state}:{checksum1}:{checksum2}";
        string responseMessage = $"[Response]:3:{function}:{state}:{length}:{sourceData}:{checksum1}:{checksum2}";
        string receiveData = $"1:{function}:{length}:{sourceData}:{checksum1}:{checksum2}";
        if (tcpServerData.portData == this.portData)
        {
            LogMonitorMainThread($"[請求]:{receiveData}");
        }

        if (isValidChecksum)
        {
            string source = $"{format}:{function}:{length}:{sourceData}:{checksum1}:{checksum2}";
            tcpServerData.sourceData = source;
            if (tcpClientdatas.TryGetValue(tcpServerData.portData.LocalPortDetails.Port, out var tcpClientData) && tcpClientData.IsConnecting)
            {
                SendMessage(tcpServerData, source);
            }
        }

        if (tcpClientdatas.ContainsKey(tcpServerData.portData.LocalPortDetails.Port))
        {
            if (tcpClientdatas[tcpServerData.portData.LocalPortDetails.Port].IsConnecting)
            {
                string stateDecimal = isValidChecksum ? "1" : "2";
                string ackMessageDecimal = $"[ACK]:DD:2:{function}:{stateDecimal}:{checksum1}:{checksum2}";
                var tcpClientData = tcpClientdatas[tcpServerData.portData.LocalPortDetails.Port];

                if (tcpClientData.portData == this.portData)
                {
                    LogMonitorMainThread(ackMessageDecimal);
                }
            }
        }

        if (state.Equals("1"))
        {
            if (tcpClientdatas.ContainsKey(tcpServerData.portData.LocalPortDetails.Port))
            {
                string stateDecimal = isValidChecksum ? "1" : "2";
                string responseMessageDecimal = $"[Response]:DD:3:{function}:{stateDecimal}:{length}:{sourceData}:{checksum1}:{checksum2}";
                var tcpClientData = tcpClientdatas[tcpServerData.portData.LocalPortDetails.Port];

                if (tcpClientData.portData == this.portData)
                {
                    LogMonitorMainThread(responseMessageDecimal);
                }
            }
        }
    }

    private List<string> SplitDataIntoGroups(string data, int groupSize)
    {
        List<string> datas = new();
        for (int i = 0; i < data.Length; i += groupSize)
        {
            string group = data.Substring(i, Math.Min(groupSize, data.Length - i));
            datas.Add(group);
        }
        return datas;
    }

    private async Task ProcessMessage(TCPServerData tcpServerData, TcpClient client, List<string> datas)
    {
        string formatHex = datas[1];
        string functionHex = datas[2];
        string lengthHex = datas[3];

        int formatDec = Convert.ToInt32(formatHex, 16);
        int functionDec = Convert.ToInt32(functionHex, 16);
        int lengthDec = Convert.ToInt32(lengthHex.StartsWith("0x") ? lengthHex[2..] : lengthHex, 16);

        string dataLengthHex = lengthHex.StartsWith("0x") ? lengthHex[2..] : lengthHex;
        int numberOfElements = Convert.ToInt32(dataLengthHex, 16);

        string dataGroupString = string.Join(":", datas.Skip(4).Take(numberOfElements));
        string dataGroupStringDec = string.Join(":", datas.Skip(4).Take(numberOfElements).Select(h => Convert.ToInt32(h, 16).ToString()));

        int checksumIndex = 4 + numberOfElements;
        string checksumHex = $"{datas[checksumIndex]}:{datas[checksumIndex + 1]}";
        string checksumDec = $"{Convert.ToInt32(datas[checksumIndex], 16)}:{Convert.ToInt32(datas[checksumIndex + 1], 16)}";
        List<byte> bytesToCheck = new()
    {
        Convert.ToByte(functionHex, 16),
        Convert.ToByte(lengthHex, 16)
    };
        bytesToCheck.AddRange(datas.Skip(4).Take(numberOfElements).Select(d => Convert.ToByte(d, 16)));

        ushort calculatedCrc = CalculateCrc16(bytesToCheck.ToArray());
        string calculatedCrcHex = $"0x{(calculatedCrc & 0xFF):X2}:0x{(calculatedCrc >> 8):X2}";

        bool isValidChecksum = checksumHex == calculatedCrcHex;

        string state = isValidChecksum ? "0x01" : "0x02";
        string ackFormat = "0x02";
        string responseFormat = "0x03";
        string start = "0xDD";
        string stop = "0x77";
        string ackMessage = $"{start}{ackFormat}{functionHex}{state}{datas[checksumIndex]}{datas[checksumIndex + 1]}{stop}";
        await SendAckOrResponse(client, ackMessage);
        string responseMessageDatas = string.Join("", datas.Skip(4).Take(numberOfElements));
        string responseMessage = $"{start}{responseFormat}{functionHex}{state}{lengthHex}{responseMessageDatas}{datas[checksumIndex]}{datas[checksumIndex + 1]}{stop}";
        if (state.Equals("0x01"))
        {
            await SendAckOrResponse(client, responseMessage);
        }
        string ack = $"0xDD:0x02:{functionHex}:{state}:{dataGroupString}:{checksumHex}:0x77";
        string response = $"0xDD:0x03:{functionHex}:{state}:{lengthHex}:{dataGroupString}:{checksumHex}:0x77";

        var decimalToack = ack
            .Split(':')
            .Select(hex => Convert.ToInt32(hex, 16))
            .ToArray();

        var decimalValuesToResponse = response
            .Split(':')
            .Select(hex => Convert.ToInt32(hex, 16))
            .ToArray();

        string ackMsg = string.Join(":", decimalToack);
        string ackResponse = string.Join (":", decimalValuesToResponse);


        if (tcpClientdatas.TryGetValue(tcpServerData.portData.LocalPortDetails.Port, out var tcpClientData) && tcpClientData.IsConnecting)
        {
            SendMessage(tcpServerData, $"[ACK]:{ackMsg}");
            SendMessage(tcpServerData, $"[Response]:{ackResponse}");
        }

        if (tcpServerData.portData == this.portData)
        {
            string reauestMsg = $"[Request]:{start}0x01{functionHex}{lengthHex}{responseMessageDatas}{datas[checksumIndex]}{datas[checksumIndex + 1]}{stop}";
            LogMonitorMainThread(reauestMsg);
        }

    }

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

    private async Task SendAckOrResponse(TcpClient client, string message)
    {
        NetworkStream stream = client.GetStream();
        if (stream.CanWrite)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }
        else
        {
            LogOnMainThread("Network stream is not writable.");
        }
    }


    /// <summary>
    /// 發送訊息到指定 IP、Port
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="tcpServerData"></param>
    private void SendMessage(TCPServerData tcpServerData, string message)
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
                var buffer = Encoding.UTF8.GetBytes(message);
                memoryStream.Write(buffer, 0, buffer.Length);
                NetworkStream stream = tcpClinetData.tcpClient.GetStream();
                memoryStream.Position = 0;
                memoryStream.CopyTo(stream);
                int packetSize = buffer.Length;
                tcpClinetData.portData.IsConnected = true;

                if(tcpClinetData.portData == this.portData)
                {
                    LogMonitorMainThread(message);
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

    #region UDP 方法

    /// <summary>
    /// 接收 UDP 訊息
    /// </summary>
    /// <param name="udpData">UDP 資料</param>
    /// <param name="port">端口</param>
    private async Task ReceiveUdpMessages(PortData portData, UdpData udpData)
    {
        using (var sendClient = new UdpClient())
        {
            sendClient.EnableBroadcast = true;
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
                        var result = await udpData.udpClient.ReceiveAsync().WithCancellation(udpData.CancellationTokenSource.Token);

                        int messageLength = result.Buffer.Length;

                        if (messageLength > buffer.Length)
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                            buffer = ArrayPool<byte>.Shared.Rent(messageLength);
                        }

                        Array.Copy(result.Buffer, buffer, messageLength);

                        string message = Encoding.UTF8.GetString(buffer, 0, messageLength);
                        portData.COMReceived += messageLength;
                        totalReceivedBytes += messageLength;

                        udpData.SourceData = message;

                        if (udpData.portData == this.portData)
                        {
                            LogMonitorMainThread($"UDP 伺服器。收到來自: {result.RemoteEndPoint} 的訊息，封包大小: {messageLength} bytes, 訊息: {message}");
                        }

                        await sendClient.SendAsync(result.Buffer, messageLength, sendEndPoint);

                        portData.NetReceived += messageLength;
                        totalSentBytes += messageLength;

                        if (udpData.portData == this.portData)
                        {
                            LogMonitorMainThread($"UDP 客戶端。傳送訊息到: {sendEndPoint}, 封包大小: {messageLength} bytes, 訊息: {message}");
                        }

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
                ArrayPool<byte>.Shared.Return(buffer); 
            }
        }

        udpData.Dispose(); 
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


