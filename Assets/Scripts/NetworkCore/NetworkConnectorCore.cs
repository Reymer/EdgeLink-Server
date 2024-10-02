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

public class NetworkConnectorCore
{
    #region 宣告
    private ConsoleUI consoleUI;
    #endregion

    #region 資料結構 TCP Client、TCP Server、UDP
    public class UdpData : IDisposable
    {
        public UdpClient udpClient;
        public CancellationTokenSource CancellationTokenSource = new();
        PortData portData;
        public bool IsConnecting;
        public string SourceData = string.Empty;
        private bool disposed = false;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancellationTokenSource.Cancel();
            udpClient?.Close();
            udpClient?.Dispose();
            CancellationTokenSource.Dispose();
        }
    }

    public class TCPServerData : IDisposable
    {
        public TcpListener TcpListener;
        public CancellationTokenSource CancellationTokenSource = new();
        public string SourceData = string.Empty;
        private bool disposed = false;
        private readonly object lockObj = new();
        public bool IsConnecting;

        public void Dispose()
        {
            lock (lockObj)
            {
                if (disposed) return;
                disposed = true;

                if (CancellationTokenSource != null && !CancellationTokenSource.IsCancellationRequested)
                {
                    CancellationTokenSource.Cancel();
                    CancellationTokenSource.Dispose();
                }

                TcpListener?.Stop();
                TcpListener?.Server?.Dispose();
            }
        }
    }

    public class TCPClinetData : IDisposable
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
        public bool IsStopped { get; set; } = false;

        public bool IsConnecting;
        public void Dispose()
        {
            lock (lockObj)
            {
                if (disposed) return;
                disposed = true;

                if (CancellationTokenSource != null && !CancellationTokenSource.IsCancellationRequested)
                {
                    CancellationTokenSource.Cancel();
                    CancellationTokenSource.Dispose();
                }
                tcpClient.Close();
                tcpClient.Dispose();
            }
        }
    }
    public void Init(ConsoleUI consoleUI)
    {
        this.consoleUI = consoleUI;
    }   

    #endregion

    #region 資料字典存取
    public enum ConnectionType { UDP, TCP }
    private readonly ConcurrentDictionary<string, UdpData> udpClients = new();
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServerdatas = new();
    private readonly ConcurrentDictionary<string, TCPClinetData> tcpClientdatas = new();

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
                DisconnectedTcpServer(portData);
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
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            portData.OnUpdate?.Invoke(portData);
        });
    }

    /// <summary>
    /// 主動斷線 TCP Server
    /// </summary>
    /// <param name="portData"></param>
    public async void DisconnectedTcpServer(PortData portData)
    {
        if (!tcpServerdatas.ContainsKey(portData.LocalPortDetails.Port))
        {
            LogOnMainThread($"未找到 TCP 伺服器，端口 {portData.LocalPortDetails.Port}");
            return;
        }

        var tcpServerData = tcpServerdatas[portData.LocalPortDetails.Port];

        if (portData.IsConnected && tcpServerData.TcpListener != null)
        {
            try
            {
                tcpServerData.CancellationTokenSource.Cancel();
                tcpServerData.IsConnecting = false;
                portData.IsConnected = false;

                await Task.Delay(100);

                tcpServerData.TcpListener.Stop();
                tcpServerData.Dispose();
                tcpServerData.TcpListener = null;

                LogOnMainThread($"已主動斷開 TCP 連接，端口 {portData.LocalPortDetails.Port}");
            }
            catch (Exception ex)
            {
                LogOnMainThread($"主動斷開連接失敗，端口 {portData.LocalPortDetails.Port}: {ex.Message}");
            }
            finally
            {
                tcpServerData.CancellationTokenSource = new CancellationTokenSource();
            }
        }

        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            portData.OnUpdate?.Invoke(portData);
        });
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

        var tcpClientData = tcpClientdatas[portData.RemotePortDetails.Port];

        if (portData.IsConnected && tcpClientData.tcpClient != null)
        {
            try
            {
                tcpClientData.CancellationTokenSource.Cancel();
                tcpClientData.IsConnecting = false;
                portData.IsConnected = false;
                await Task.Delay(100);
                tcpClientData.tcpClient.Close();
                tcpClientData.tcpClient.Dispose();
                tcpClientData.tcpClient = null;

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
        }
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            portData.OnUpdate?.Invoke(portData);
        });
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
            tcpClientData = new TCPClinetData
            {
                portData = portData,
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
    private async Task ConnectTcpClient(TCPClinetData tcpClientData, PortData portData)
    {
        try
        {
            var tcpClient = new TcpClient();
            var token = tcpClientData.CancellationTokenSource.Token;
            if (token.IsCancellationRequested)
            {
                LogOnMainThread($"TCP 客戶端連接已被取消，端口 {portData.RemotePortDetails.Port}");
                return;
            }

            var connectTask = tcpClient.ConnectAsync(portData.TargetIP, int.Parse(portData.RemotePortDetails.Port));
            var timeout = Task.Delay(1000, token);
            var completedTask = await Task.WhenAny(connectTask, timeout);

            if (completedTask == timeout)
            {
                portData.IsConnected = false;
                tcpClientData.IsConnecting = false;
                LogOnMainThread($"TCP 客戶端連接失敗，端口 {portData.RemotePortDetails.Port}");
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    portData.OnUpdate?.Invoke(portData);
                });
                return;
            }

            if (token.IsCancellationRequested)
            {
                LogOnMainThread($"TCP 客戶端已連接但立即取消，端口 {portData.RemotePortDetails.Port}");
                tcpClient.Close();
                return;
            }

            if (connectTask.IsFaulted)
            {
                throw connectTask.Exception ?? new Exception("Unknown connection failure.");
            }

            tcpClientData.tcpClient = tcpClient;
            portData.IsConnected = true;
            tcpClientData.IsConnecting = true;

            LogOnMainThread($"已連接到 TCP 客戶端: {portData.TargetIP}:{portData.RemotePortDetails.Port}");
            StartConnectionMonitoring(tcpClientData, portData);
        }
        catch (Exception ex)
        {
            portData.IsConnected = false;
            tcpClientData.IsConnecting = false;
            LogOnMainThread($"TCP 客戶端 {portData.TargetIP}:{portData.RemotePortDetails.Port} 連接失敗: {ex.Message}。");
        }

        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            portData.OnUpdate?.Invoke(portData);
        });
    }

    /// <summary>
    /// TCP Client 斷線判斷
    /// </summary>
    /// <param name="tcpClientData"></param>
    /// <param name="portData"></param>
    private void StartConnectionMonitoring(TCPClinetData tcpClientData, PortData portData)
    {
        var token = tcpClientData.CancellationTokenSource.Token;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000);

                    if (tcpClientData.tcpClient == null || !tcpClientData.tcpClient.Connected)
                    {
                        break; // 退出循环
                    }

                    if (tcpClientData.tcpClient.Client.Poll(0, SelectMode.SelectRead) && tcpClientData.tcpClient.Client.Available == 0)
                    {
                        LogOnMainThread($"TCP 客戶端連接已斷開，端口 {portData.RemotePortDetails.Port}");
                        tcpClientData.tcpClient.Close();
                        tcpClientData.tcpClient = null;
                        portData.IsConnected = false;
                        tcpClientData.IsConnecting = false;

                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        {
                            portData.OnUpdate?.Invoke(portData);
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnMainThread($"TCP 客戶端連接狀態檢測時發生異常: {ex.Message}");
                portData.IsConnected = false;
                tcpClientData.IsConnecting = false;
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    portData.OnUpdate?.Invoke(portData);
                });
            }
        });
    }


    /// <summary>
    /// 新增 TCP Server
    /// </summary>
    /// <param name="remotePort">要新增的端口</param>
    private void AddTcpListener(PortData portData)
    {
        if (tcpServerdatas.TryGetValue(portData.LocalPortDetails.Port, out var existingServerData))
        {
            if (existingServerData.IsConnecting)
            {
                LogOnMainThread($"端口 {portData.LocalPortDetails.Port} 已經存在 TCP 伺服器，正在連接中。");
                return;
            }

            try
            {
                existingServerData.CancellationTokenSource?.Cancel();

                existingServerData.TcpListener?.Stop();
                existingServerData.IsConnecting = false;
                LogOnMainThread($"端口 {portData.LocalPortDetails.Port} 的 TCP 伺服器已停止，準備重新啟動。");
                Task.Delay(100).Wait();
            }
            catch (Exception ex)
            {
                LogOnMainThread($"停止端口 {portData.LocalPortDetails.Port} 的 TCP 伺服器時發生錯誤: {ex.Message}", isError: true);
            }
        }

        try
        {
            var tcpListener = new TcpListener(IPAddress.Any, int.Parse(portData.LocalPortDetails.Port));
            tcpListener.Start();

            portData.IsConnected = true;
            existingServerData = new TCPServerData
            {
                TcpListener = tcpListener,
                CancellationTokenSource = new CancellationTokenSource(),
            };

            tcpServerdatas[portData.LocalPortDetails.Port] = existingServerData;

            LogOnMainThread($"在端口 {portData.LocalPortDetails.Port} 上啟動了 TCP 伺服器端。");

            Task.Run(() => ListenForTcpClients(portData, existingServerData));

            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                portData.OnUpdate?.Invoke(portData);
            });
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            LogOnMainThread($"端口 {portData.LocalPortDetails.Port} 已經被使用。", isError: true);
        }
        catch (Exception ex)
        {
            LogOnMainThread($"初始化端口 {portData.LocalPortDetails.Port} 的 TCP 監聽器時發生錯誤: {ex.Message}", isError: true);
        }
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
            var udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, int.Parse(portData.RemotePortDetails.Port)));
            portData.IsConnected = true;
            existingServerData = new UdpData
            {
                udpClient = udpClient,
                CancellationTokenSource = new CancellationTokenSource(),
            };

            udpClients[portData.RemotePortDetails.Port] = existingServerData;

            LogOnMainThread($"在端口 {portData.RemotePortDetails.Port} 上啟動了 UDP 伺服器端。");

            Task.Run(() => ReceiveUdpMessages(portData, existingServerData));

            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                portData.OnUpdate?.Invoke(portData);
            });
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
    public void StopClient(string port, string connectionType)
    {
        try
        {
            if (connectionType.Equals("UDP", StringComparison.OrdinalIgnoreCase))
            {
                DisposeClientResources(port, udpClients, "UDP");
            }
            else if (connectionType.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
            {
                DisposeClientResources(port, tcpServerdatas, "TCP");
            }
            else if (connectionType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
            {
                DisposeClientResources(port, tcpClientdatas, "TCP");
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"停止客戶端時出現錯誤: {ex.Message}");
        }
    }
    private void DisposeClientResources<T>(string port, ConcurrentDictionary<string, T> clientDictionary, string protocol) where T : IDisposable
    {
        if (clientDictionary.TryRemove(port, out T clientData))
        {
            if (clientData is UdpData udpData)
            {
                udpData.CancellationTokenSource.Cancel();
                LogOnMainThread($"已刪除 UDP 伺服器端口 {port}。");
            }
            else if (clientData is TCPServerData tcpServerData)
            {
                tcpServerData.CancellationTokenSource.Cancel();
                LogOnMainThread($"已刪除 TCP 伺服器端口 {port}。");
            }
            else if (clientData is TCPClinetData tcpClientData)
            {
                tcpClientData.CancellationTokenSource.Cancel();
                LogOnMainThread($"已刪除 TCP 客戶端端口 {port}。");
            }
            clientData.Dispose();
        }
        else
        {
            LogOnMainThread($"無法找到端口 {port} 上的 {protocol} 客戶端。", isError: true);
        }
    }

    #endregion

    #region TCP 方法

    private async Task ListenForTcpClients(PortData portData, TCPServerData tcpServerData)
    {
        try
        {
            while (!tcpServerData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    tcpServerData.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    var client = await tcpServerData.TcpListener.AcceptTcpClientAsync().WithCancellation(tcpServerData.CancellationTokenSource.Token);
                    await ReceiveTcpMessages(portData, client, tcpServerData);
                }
                catch (OperationCanceledException)
                {
                    Debug.Log("TCP 監視器已取消。");
                }
                catch (ObjectDisposedException)
                {
                    Debug.Log("TCP 監視器已被處置。");
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.Interrupted)
                {
                    Debug.Log($"Socket 中斷: {se.Message}");
                }
                catch (Exception ex)
                {
                    Debug.Log($"接受 TCP 客戶端錯誤: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogOnMainThread("TCP 監聽器已取消。");
        }
        catch (ObjectDisposedException)
        {
            LogOnMainThread("TCP 監聽器已被處置。");
        }
        catch (Exception ex)
        {
            LogOnMainThread($"TCP 監聽器遇到錯誤: {ex.Message}", isError: true);
        }
        finally
        {
            lock (tcpServerData)
            {
                if (tcpServerData != null && !tcpServerData.CancellationTokenSource.IsCancellationRequested)
                {
                    tcpServerData.CancellationTokenSource.Cancel();
                }
                tcpServerData.Dispose();
            }
        }
    }

    
    private async Task ReceiveTcpMessages(PortData portData, TcpClient client, TCPServerData tcpServerData)
    {
        IPEndPoint remoteEndPoint = client.Client.LocalEndPoint as IPEndPoint;
        string sourceIP = remoteEndPoint?.Address.ToString();
        int sourcePort = remoteEndPoint?.Port ?? 0;

        try
        {
            var buffer = new byte[2048];
            using NetworkStream stream = client.GetStream();

            while (!tcpServerData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, tcpServerData.CancellationTokenSource.Token);

                    if (bytesRead > 0 && portData.IsConnected)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        tcpServerData.SourceData = message;

                        LogOnMainThread($"TCP 伺服器。收到訊息來自: {remoteEndPoint}, 訊息: {message}");
                        if (tcpClientdatas.ContainsKey(portData.LocalPortDetails.Port))
                        {
                            var tcpClinetData = tcpClientdatas[portData.LocalPortDetails.Port];
                            if (tcpClinetData.IsConnecting)
                            {
                                SendMessage(portData, tcpServerData);
                            }
                        }
                    }
                    else
                    {
                        LogOnMainThread($"TCP 伺服器端 {remoteEndPoint} 已斷開連接。");
                        break;
                    }
                }
                catch (IOException readEx)
                {
                    LogOnMainThread($"接收 TCP 訊息時出現錯誤: {readEx.Message}", isError: true);
                    break;
                }
            }
        }
        catch (IOException ex)
        {
            LogOnMainThread($"接收 TCP 訊息時出現錯誤: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 發送訊息到指定 IP、Port
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="tcpServerData"></param>
    private void SendMessage(PortData portData, TCPServerData tcpServerData)
    {
        if (!tcpClientdatas.ContainsKey(portData.LocalPortDetails.Port))
        {
            Debug.Log($"沒有可用的 TCP 客戶端!");
            return;
        }
        var tcpClinetData = tcpClientdatas[portData.LocalPortDetails.Port];
        if (tcpClinetData.IsConnecting)
        {
            try
            {
                var buffer = Encoding.UTF8.GetBytes(tcpServerData.SourceData);
                NetworkStream stream = tcpClinetData.tcpClient.GetStream();
                stream.Write(buffer, 0, buffer.Length);
                tcpClinetData.IsConnecting = true;
                LogOnMainThread($"TCP 客戶端。傳送訊息到達: {tcpClinetData.tcpClient.Client.RemoteEndPoint}, 訊息: {tcpServerData.SourceData}");
            }
            catch (Exception ex)
            {

                LogOnMainThread($"發送訊息到 TCP 客戶端時出現錯誤: {ex.Message}", isError: true);
            }
        }
        else
        {
            tcpClinetData.IsConnecting = false;
            portData.IsConnected = false;
            tcpClinetData.portData.IsConnected = tcpClinetData.IsConnecting;
            LogOnMainThread($"來自 {tcpClinetData.tcpClient.Client.RemoteEndPoint} TCP 客戶端已斷開連接: ", isError: true);
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                tcpClinetData.portData.OnUpdate?.Invoke(tcpClinetData.portData);
            });
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
        using (var sendClient = new UdpClient())
        {
            IPEndPoint sendEndPoint = new(IPAddress.Loopback, int.Parse(portData.LocalPortDetails.Port));

            while (!udpData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await udpData.udpClient.ReceiveAsync().WithCancellation(udpData.CancellationTokenSource.Token);
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    int messageLength = result.Buffer.Length;
                    portData.COMReceived += messageLength;
                    udpData.SourceData = message;
                    LogOnMainThread($"UDP 伺服器。收到訊息來自: {result.RemoteEndPoint}, 訊息: {message}");          

                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await sendClient.SendAsync(messageBytes, messageBytes.Length, sendEndPoint);
                    portData.NetReceived += messageBytes.Length;
                    LogOnMainThread($"UDP 客戶端。傳送訊息到達: {portData.LocalPortDetails.Port}, 訊息: {message}");
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        portData.OnUpdate?.Invoke(portData);
                    });
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
                sourceData = tcpData.SourceData;
                tcpData.SourceData = string.Empty;
            }
        }

        if (!string.IsNullOrEmpty(sourceData))
        {
            return sourceData.Length > length ? sourceData.Substring(0, length) : sourceData;
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

        Debug.Log("All clients successfully shut down.");
    }
    #endregion

    #region 日誌封裝
    private void LogOnMainThread(string message, bool isError = false)
    {
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


