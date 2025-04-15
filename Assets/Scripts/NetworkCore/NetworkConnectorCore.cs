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
                if (CancellationTokenSource != null)
                {
                    if (!CancellationTokenSource.IsCancellationRequested)
                        CancellationTokenSource.Cancel();

                    CancellationTokenSource.Dispose();
                    CancellationTokenSource = null;
                }

                if (udpClient != null)
                {
                    try { udpClient.Close(); } catch { }
                    try { udpClient.Dispose(); } catch { }
                    udpClient = null;
                }
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

        private int recvCount = 0;
        private int sendCount = 0;
        public int RecvCount => recvCount;
        public int SendCount => sendCount;

        public bool disposed = false;
        private readonly object lockObj = new();

        public void IncrementRecvCount()
        {
            Interlocked.Increment(ref recvCount);
        }
        public void IncrementSendCount()
        {
            Interlocked.Increment(ref sendCount);
        }
        public void Dispose()
        {
            DisposeResources(ref disposed, lockObj, () =>
            {
                if (CancellationTokenSource != null)
                {
                    if (!CancellationTokenSource.IsCancellationRequested)
                        CancellationTokenSource.Cancel();

                    CancellationTokenSource.Dispose();
                    CancellationTokenSource = null; // ✅ 防止重複釋放
                }

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
                if (CancellationTokenSource != null)
                {
                    if (!CancellationTokenSource.IsCancellationRequested)
                        CancellationTokenSource.Cancel();

                    try { CancellationTokenSource.Dispose(); } catch { }
                    CancellationTokenSource = null;
                }

                if (tcpClient != null)
                {
                    try { tcpClient.Close(); } catch { }
                    try { tcpClient.Dispose(); } catch { }
                    tcpClient = null;
                }
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
        await SafeAsync(async () =>
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
        }, "AddTcpClient");
    }


    /// <summary>
    /// 主動連線 TCP Client
    /// </summary>
    /// <param name="tcpClientData"></param>
    /// <param name="portData"></param>
    /// <returns></returns>
    private async Task ConnectTcpClient(TCPClientData tcpClientData, PortData portData)
    {
        await SafeAsync(async () =>
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
                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            Safe(() => portData.OnUpdate?.Invoke(portData), "ConnectTcpClient.OnUpdate.Timeout"));
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

                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        Safe(() => portData.OnUpdate?.Invoke(portData), "ConnectTcpClient.OnUpdate.Error"));
                    await Task.Delay(reconnectDelay);
                }
            }

            UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                Safe(() => portData.OnUpdate?.Invoke(portData), "ConnectTcpClient.OnUpdate.Final"));
        }, "ConnectTcpClient");
    }

    /// <summary>
    /// 開始監控 TCP 連接狀態
    /// </summary>
    /// <param name="tcpClientData"></param>
    /// <param name="portData"></param>
    private void StartConnectionMonitoring(TCPClientData tcpClientData, PortData portData)
    {
        var token = tcpClientData.CancellationTokenSource.Token;
        int reconnectDelay = 5000;

        Task.Run(async () =>
        {
            await SafeAsync(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000);

                    if (tcpClientData.tcpClient == null ||
                        !tcpClientData.tcpClient.Connected ||
                        (tcpClientData.tcpClient.Client.Poll(0, SelectMode.SelectRead) && tcpClientData.tcpClient.Client.Available == 0))
                    {
                        LogOnMainThread($"TCP 客戶端連接已斷開，目標端口: {portData.RemotePortDetails.Port}");

                        Safe(() => tcpClientData.tcpClient?.Close(), "Monitoring.CloseClient");
                        tcpClientData.tcpClient = null;
                        portData.IsConnected = false;
                        tcpClientData.IsConnecting = false;

                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                            Safe(() => portData.OnUpdate?.Invoke(portData), "Monitoring.OnUpdate.Disconnect"));

                        // ➤ 嘗試重連邏輯
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

                                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                                        Safe(() => portData.OnUpdate?.Invoke(portData), "Monitoring.OnUpdate.Reconnect"));

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
            }, "StartConnectionMonitoring.Loop");
        }, token);
    }


    /// <summary>
    /// 新增 UDP 客戶端
    /// </summary>
    /// <param name="portData"></param>
    private void AddUdpClient(PortData portData)
    {
        Safe(() =>
        {
            if (udpClients.TryGetValue(portData.ProtocolName, out var existingServerData))
            {
                if (existingServerData.IsConnecting)
                {
                    LogOnMainThread($"端口 {portData.RemotePortDetails.Port} 已經存在 UDP 伺服器，正在連接中。");
                    return;
                }

                Safe(() => existingServerData.CancellationTokenSource?.Cancel(), "AddUdpClient.Cancel");
                Safe(() => existingServerData.udpClient?.Dispose(), "AddUdpClient.DisposeOldUDP");
                existingServerData.udpClient = null;
                existingServerData.IsConnecting = false;
                Safe(() => Task.Delay(100).Wait(), "AddUdpClient.WaitDispose");
            }

            try
            {
                var udpClient = new UdpClient(int.Parse(portData.RemotePortDetails.Port));
                portData.IsConnected = true;

                var newServerData = new UdpData
                {
                    portData = portData,
                    udpClient = udpClient,
                    CancellationTokenSource = new CancellationTokenSource(),
                    IsConnecting = true
                };

                udpClients[portData.ProtocolName] = newServerData;

                LogOnMainThread($"在端口 {portData.RemotePortDetails.Port} 上啟動了 UDP 伺服器端。");

                Task.Run(() => SafeAsync(() => ReceiveUdpMessages(portData, newServerData), "ReceiveUdpMessages"));

                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    Safe(() => portData.OnUpdate?.Invoke(portData), "AddUdpClient.OnUpdate"));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                LogOnMainThread($"端口 {portData.RemotePortDetails.Port} 已經被使用。", isError: true);
            }
            catch (Exception ex)
            {
                LogOnMainThread($"初始化端口 {portData.RemotePortDetails.Port} 的 UDP 監聽器時發生錯誤: {ex.Message}", isError: true);
            }

        }, "AddUdpClient");
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
        Safe(() =>
        {
            string protocol = portData.NetProtocol?.Trim().ToUpperInvariant();

            switch (protocol)
            {
                case "UDP":
                    Safe(() => DisconnectedUdp(portData), "StopClient.UDP");
                    break;

                case "TCP SERVER":
                    Safe(() => DisposeTcpServer(portData), "StopClient.TCPServer");
                    break;

                case "TCP CLIENT":
                    Safe(() => DisconnectedTcpClient(portData), "StopClient.TCPClient");
                    break;

                default:
                    LogOnMainThread($"無法識別的連接類型: {portData.NetProtocol}", isError: true);
                    break;
            }
        }, "StopClient");
    }


    #endregion

    #region TCP 方法

    /// <summary>
    /// 新增 TCP 監聽器。
    /// </summary>
    /// <param name="portData"></param>
    private void AddTcpListener(PortData portData)
    {
        Safe(() =>
        {
            if (!tcpServerdatas.TryGetValue(portData.ProtocolName, out TCPServerData tcpServerData))
            {
                Safe(() => StartTcpListener(portData), "AddTcpListener.Start");
            }
            else
            {
                Safe(() => RestartTcpListener(tcpServerData), "AddTcpListener.Restart");
            }
        }, "AddTcpListener");
    }


    /// <summary>
    /// 啟動 TCP 監聽器。
    /// </summary>
    /// <param name="portData"></param>
    private void StartTcpListener(PortData portData)
    {
        Safe(() =>
        {
            TcpListener tcpListener = null;

            try
            {
                int port = int.Parse(portData.LocalPortDetails.Port);
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();

                var tcpServerData = new TCPServerData
                {
                    portData = portData,
                    tcpListener = tcpListener,
                    CancellationTokenSource = new CancellationTokenSource(),
                };

                tcpServerdatas.TryAdd(portData.ProtocolName, tcpServerData);
                LogOnMainThread($"在端口 {port} 上啟動了 TCP 伺服器端。");

                Task.Run(() => SafeAsync(() => ListenForTcpClients(tcpServerData), "ListenForTcpClients"));
            }
            catch (Exception ex)
            {
                LogOnMainThread($"啟動 TCP 監聽器時發生錯誤: {ex.Message}", isError: true);
                Safe(() => tcpListener?.Stop(), "StartTcpListener.StopAfterError");
            }
        }, "StartTcpListener");
    }


    /// <summary>
    /// 重新啟動 TCP 監聽器。
    /// </summary>
    /// <param name="tcpServerData"></param>

    private void RestartTcpListener(TCPServerData tcpServerData)
    {
        Safe(() =>
        {
            // 🔒 安全取消舊的 CancellationToken
            Safe(() =>
            {
                if (tcpServerData.CancellationTokenSource != null &&
                    !tcpServerData.CancellationTokenSource.IsCancellationRequested)
                {
                    tcpServerData.CancellationTokenSource.Cancel();
                    tcpServerData.CancellationTokenSource.Dispose();
                }
            }, "RestartTcpListener.CancelDisposeOldToken");

            // ✅ 重建 Token
            tcpServerData.CancellationTokenSource = new CancellationTokenSource();
            tcpServerData.portData.IsConnected = true;

            // ✅ UI 更新
            Safe(() =>
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    Safe(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData), "RestartTcpListener.OnUpdate"));
            }, "RestartTcpListener.Enqueue");

            // ✅ 啟動新監聽任務
            Task.Run(() => SafeAsync(() => ListenForTcpClients(tcpServerData), "ListenForTcpClients"));

            LogOnMainThread($"[Restart] TCP 伺服器重啟監聽中，端口：{tcpServerData.portData.LocalPortDetails.Port}");
        }, "RestartTcpListener");
    }


    /// <summary>
    /// 釋放 TCP 伺服器。
    /// </summary>
    /// <param name="portData"></param>
    private void DisposeTcpServer(PortData portData)
    {
        Safe(() =>
        {
            string portKey = portData.ProtocolName;

            if (tcpServerdatas.TryRemove(portKey, out TCPServerData tcpServerData))
            {
                Safe(() =>
                {
                    if (tcpServerData.CancellationTokenSource != null &&
                        !tcpServerData.CancellationTokenSource.IsCancellationRequested)
                    {
                        tcpServerData.CancellationTokenSource.Cancel();
                        tcpServerData.CancellationTokenSource.Dispose();
                        tcpServerData.CancellationTokenSource = null; // ✅ 補這一行
                    }
                }, $"DisposeTcpServer.CancelToken:{portKey}");

                Safe(() =>
                {
                    tcpServerData.tcpListener?.Stop();
                    tcpServerData.tcpListener = null;
                }, $"DisposeTcpServer.StopListener:{portKey}");

                Safe(() =>
                {
                    tcpServerData.Dispose();
                }, $"DisposeTcpServer.DisposeObject:{portKey}");

                LogOnMainThread($"已釋放 TCP Server：{portKey}");
            }
            else
            {
                LogOnMainThread($"未找到 TCP Server 可釋放：{portKey}", isError: true);
            }
        }, "DisposeTcpServer");
    }


    /// <summary>
    /// 斷開 TCP 伺服器。
    /// </summary>
    /// <param name="portData"></param>
    private void DisconnectTcpServer(PortData portData)
    {
        Safe(() =>
        {
            string key = portData.ProtocolName;

            if (tcpServerdatas.TryGetValue(key, out TCPServerData tcpServerData))
            {
                Safe(() =>
                {
                    tcpServerData.CancellationTokenSource?.Cancel();
                    tcpServerData.portData.IsConnected = false;
                }, $"DisconnectTcpServer.Cancel:{key}");

                Safe(() =>
                {
                    UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                        Safe(() => portData.OnUpdate?.Invoke(portData), $"DisconnectTcpServer.OnUpdate:{key}")
                    );
                }, $"DisconnectTcpServer.Enqueue:{key}");

                LogOnMainThread($"已中斷 TCP Server：{key}");
            }
            else
            {
                LogOnMainThread($"未找到 TCP Server：{key}", isError: true);
            }
        }, "DisconnectTcpServer");
    }

    /// <summary>
    /// 監聽 TCP 客戶端。
    /// </summary>
    /// <param name="tcpServerData"></param>
    /// <returns></returns>
    private async Task ListenForTcpClients(TCPServerData tcpServerData)
    {
        string portName = tcpServerData.portData?.ProtocolName ?? "Unknown";

        await SafeAsync(async () =>
        {
            while (!tcpServerData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await tcpServerData.tcpListener.AcceptTcpClientAsync();

                    Safe(() =>
                    {
                        tcpServerData.RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                        tcpServerData.portData.IsConnected = true;

                        LogOnMainThread($"[TCP:{portName}] 新客戶端連線：{tcpServerData.RemoteEndPoint}");
                        SendClientEventMessage(tcpServerData, "CONNECTED");

                        UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                            Safe(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData), $"Listen.OnUpdate:{portName}")
                        );
                    }, $"Listen.HandleConnection:{portName}");

                    _ = Task.Run(() => SafeAsync(() => ReceiveTcpMessages(client, tcpServerData), $"ReceiveTcpMessages:{portName}"));
                    _ = Task.Run(() => SafeAsync(() => ProcessTcpPackets(tcpServerData), $"ProcessTcpPackets:{portName}"));
                }
                catch (SocketException ex)
                {
                    LogOnMainThread($"[TCP:{portName}] SocketException: {ex.Message}", isError: true);
                }
                catch (ObjectDisposedException)
                {
                    LogOnMainThread($"[TCP:{portName}] TcpListener 已被釋放，結束監聽。");
                    break;
                }
                catch (Exception ex)
                {
                    LogOnMainThread($"[TCP:{portName}] 接收連線時發生例外: {ex.Message}", isError: true);
                }
            }
        }, $"ListenForTcpClients:{portName}");

        LogOnMainThread($"[TCP:{portName}] TCP 伺服器監聽任務已結束");
    }


    /// <summary>
    /// 接收來自 TCP 客戶端的訊息。
    /// </summary>
    /// <param name="client"></param>
    /// <param name="tcpServerData"></param>
    /// <returns></returns>
    private async Task ReceiveTcpMessages(TcpClient client, TCPServerData tcpServerData)
    {
        string endpoint = tcpServerData.RemoteEndPoint?.ToString() ?? "Unknown";
        var token = tcpServerData.CancellationTokenSource.Token;

        await SafeAsync(async () =>
        {
            tcpServerData.RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[2048];

            while (!token.IsCancellationRequested)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead <= 0)
                    {
                        LogOnMainThread($"[TCP:{tcpServerData.portData.ProtocolName}] 客戶端 {endpoint} 已斷開連接。");
                        break;
                    }

                    byte[] packet = new byte[bytesRead];
                    Array.Copy(buffer, 0, packet, 0, bytesRead);
                    tcpServerData.asyncMessageQueue.Enqueue(packet);
                }
                catch (OperationCanceledException)
                {
                    LogOnMainThread($"[TCP:Recv] 接收被取消：{endpoint}");
                    break;
                }
                catch (Exception ex)
                {
                    LogOnMainThread($"[TCP:Recv] 接收資料錯誤 {endpoint}：{ex.Message}", isError: true);
                    break;
                }
            }
        }, $"ReceiveTcpMessages:{endpoint}");

        // ✅ 連線中斷後的清理
        Safe(() =>
        {
            tcpServerData.portData.IsConnected = false;
            client?.Close();
            SendClientEventMessage(tcpServerData, "DISCONNECTED", "Remote closed or error");
            UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                Safe(() => tcpServerData.portData.OnUpdate?.Invoke(tcpServerData.portData), $"OnUpdate:{endpoint}")
            );
        }, $"ReceiveTcpMessages.Cleanup:{endpoint}");
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

        await SafeAsync(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                byte[] packet;
                try
                {
                    packet = await tcpServerData.asyncMessageQueue.DequeueAsync(token);
                }
                catch (OperationCanceledException)
                {
                    LogOnMainThread($"[{source}] 封包處理取消 (Dequeue)");
                    break;
                }

                string receivedData = string.Empty;
                try
                {
                    receivedData = Encoding.UTF8.GetString(packet);
                    dataBuffer.Append(receivedData);
                }
                catch (Exception decodeEx)
                {
                    LogOnMainThread($"[{source}] UTF8 轉換錯誤: {decodeEx.Message}", isError: true);
                    continue;
                }

                string bufferString = dataBuffer.ToString();
                int lastNewlineIndex = bufferString.LastIndexOf('\n');

                if (lastNewlineIndex < 0)
                    continue;

                string processable = bufferString.Substring(0, lastNewlineIndex);
                string remaining = bufferString.Substring(lastNewlineIndex + 1);
                dataBuffer.Clear();
                dataBuffer.Append(remaining);

                foreach (var packetStr in processable.Split('\n'))
                {
                    string completePacket = packetStr.Trim();
                    if (string.IsNullOrWhiteSpace(completePacket))
                        continue;

                    int packetSize = Encoding.UTF8.GetByteCount(completePacket) + 1;

                    try
                    {
                        string currentMaskType = tcpServerData.portData.MaskType;

                        switch (currentMaskType)
                        {
                            case "Robot to 10":
                                var packetBytes = Encoding.UTF8.GetBytes(completePacket);
                                HandleRobotTo10Message(tcpServerData, packetBytes, packetSize);
                                break;

                            case "Robot to 16":
                                ProcessRobotTo16Message(completePacket, tcpServerData);
                                break;

                            case "original data":
                                byte[] originalBytes = Encoding.UTF8.GetBytes(completePacket);
                                HandleOriginalDataMessage(tcpServerData, completePacket, packetSize);
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
            }
        }, $"ProcessTcpPackets:{source}");
    }

    /// <summary>
    /// 發送事件消息到 TCP 客戶端。
    /// </summary>
    /// <param name="serverData"></param>
    /// <param name="eventType"></param>
    /// <param name="reason"></param>
    private void SendClientEventMessage(TCPServerData serverData, string eventType, string reason = null)
    {
        try
        {
            if (tcpClientdatas.TryGetValue(serverData.portData.ProtocolName, out var clientData) &&
                clientData.IsConnecting &&
                clientData.tcpClient?.Connected == true)
            {
                NetworkStream stream = clientData.tcpClient.GetStream();

                if (stream.CanWrite)
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(eventType + "\n");
                    stream.Write(buffer, 0, buffer.Length);
                    LogMonitorMainThread($"主動通知 Client: {eventType}");
                }
                else
                {
                    LogOnMainThread("⚠️ 無法寫入 TCP Stream（可能已斷線）", isError: true);
                }
            }
            else
            {
                LogOnMainThread($"⚠️ Client 無連線或已關閉，無法發送事件 {eventType}", isError: true);
            }
        }
        catch (Exception ex)
        {
            LogOnMainThread($"發送事件訊息 {eventType} 時發生錯誤: {ex.Message}", isError: true);
        }
    }


    /// <summary>
    /// 處理來自機器人的消息。
    /// </summary>
    /// <param name="tcpServerData"></param>
    /// <param name="buffer"></param>
    /// <param name="bytesRead"></param>
    private void HandleRobotTo10Message(TCPServerData tcpServerData, byte[] buffer, int bytesRead)
    {
        try
        {
            byte[] messageData = new byte[bytesRead];
            Array.Copy(buffer, 0, messageData, 0, bytesRead);

            if (messageData.Length >= 7 && messageData[0] == 0xDD && messageData[^1] == 0x77)
            {
                try
                {
                    ProcessMessage(messageData);
                }
                catch (Exception innerEx)
                {
                    LogOnMainThread($"處理 RobotTo10 訊息時發生錯誤: {innerEx.Message}", isError: true);
                }
            }
            else if (tcpServerData.portData == this.portData)
            {
                string hexData = BitConverter.ToString(messageData).Replace("-", " ");
                LogMonitorMainThread($"⚠️ 錯誤：收到的資料不以 0xDD 開頭或以 0x77 結尾。資料: {hexData}");
            }
        }
        catch (Exception ex)
        {
            LogOnMainThread($"HandleRobotTo10Message 發生例外錯誤: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 處理原始資料消息。
    /// </summary>
    /// <param name="tcpServerData"></param>
    /// <param name="completePacket"></param>
    /// <param name="packetSize"></param>
    private void HandleOriginalDataMessage(TCPServerData tcpServerData, string completePacket, int packetSize)
    {
        try
        {
            if (tcpServerData.portData == this.portData)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                tcpServerData.IncrementRecvCount();
                LogMonitorMainThread($"[{timestamp}] 收到訊息，資料大小: {packetSize}，資料: {completePacket}，發送次數: {tcpServerData.RecvCount}");
            }

            if (!tcpClientdatas.TryGetValue(tcpServerData.portData.ProtocolName, out var tcpClientData))
                return;

            if (!tcpClientData.IsConnecting)
                return;

            try
            {
                SendMessage(tcpServerData, completePacket);
            }
            catch (Exception sendEx)
            {
                LogOnMainThread($"原始資料轉發失敗: {sendEx.Message}", isError: true);
            }
        }
        catch (Exception ex)
        {
            LogOnMainThread($"HandleOriginalDataMessage 發生例外錯誤: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 處理來自機器人的消息，將其轉換為 16 進制格式。
    /// </summary>
    /// <param name="data"></param>
    /// <param name="tcpServerData"></param>
    private void ProcessRobotTo16Message(string data, TCPServerData tcpServerData)
    {
        try
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
                sourceData = function switch
                {
                    1 => $"{ConvertToIEEE754Hexadecimal(datas[3])}:{ConvertToIEEE754Hexadecimal(datas[4])}",
                    2 => ConvertToIEEE754Hexadecimal(datas[3]),
                    3 or 7 or 8 or 10 => "0x" + int.Parse(datas[3]).ToString("X2"),
                    _ => string.Empty
                };
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
                        bytesToCheck.Add(Convert.ToByte(hex.Trim()[2..], 16)); // Trim "0x"
                }
            }
            catch (Exception ex)
            {
                LogMonitorMainThread($"[錯誤]: 構建校驗數據時發生錯誤 - {ex.Message}");
                return;
            }

            ushort crc = CalculateCrc16(bytesToCheck.ToArray());
            int checksum1 = crc & 0xFF;
            int checksum2 = (crc >> 8) & 0xFF;

            string start = "0xDD";
            string stop = "0x77";
            string formatHex = $"0x{format:X2}";
            string functionHex = $"0x{function:X2}";
            string lengthHex = $"0x{length:X2}";
            string checksum1Hex = $"0x{checksum1:X2}";
            string checksum2Hex = $"0x{checksum2:X2}";

            string payload = function switch
            {
                1 or 2 => RemoveColons(sourceData),
                _ => sourceData
            };

            string hexString = length == 0
                ? $"{start}{formatHex}{functionHex}{lengthHex}{checksum1Hex}{checksum2Hex}{stop}"
                : $"{start}{formatHex}{functionHex}{lengthHex}{payload}{checksum1Hex}{checksum2Hex}{stop}";

            List<byte> byteList = new();
            try
            {
                for (int i = 0; i < hexString.Length; i += 4)
                {
                    if (i + 4 > hexString.Length) break;
                    string hexVal = hexString.Substring(i + 2, 2);
                    byteList.Add(Convert.ToByte(hexVal, 16));
                }
            }
            catch (Exception ex)
            {
                LogMonitorMainThread($"[錯誤]: 生成 ByteList 時發生錯誤 - {ex.Message}");
                return;
            }

            tcpServerData.sourceData = hexString;

            if (tcpClientdatas.TryGetValue(tcpServerData.portData.ProtocolName, out var tcpClientData) && tcpClientData.IsConnecting)
            {
                try
                {
                    SendMessage(tcpServerData, byteList.ToArray());
                }
                catch (Exception sendEx)
                {
                    LogMonitorMainThread($"[錯誤]: 發送封包失敗 - {sendEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogMonitorMainThread($"[錯誤]: ProcessRobotTo16Message 發生未預期錯誤 - {ex.Message}");
        }
    }

    /// <summary>
    /// 將十進制數字轉換為 IEEE 754 十六進制格式。
    /// </summary>
    /// <param name="decimalString"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    private string ConvertToIEEE754Hexadecimal(string decimalString)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(decimalString))
            {
                throw new ArgumentException("輸入字串為空或空白");
            }

            // 用 CultureInfo.InvariantCulture 避免小數點逗號問題（歐洲語系）
            if (!float.TryParse(decimalString, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float decimalNumber))
            {
                throw new ArgumentException($"無法解析浮點數：{decimalString}");
            }

            byte[] bytes = BitConverter.GetBytes(decimalNumber);

            // 轉成 IEEE 754 的 HEX 表示，用冒號分隔
            string ieeeHex = string.Join(":", bytes.Select(b => $"0x{b:X2}"));
            return ieeeHex;
        }
        catch (Exception ex)
        {
            LogMonitorMainThread($"[錯誤] IEEE754 轉換失敗: {ex.Message}", isError: true);
            return string.Empty;
        }
    }

    /// <summary>
    /// 移除十六進制數字中的冒號。
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    private string RemoveColons(string input)
    {
        return input.Replace(":", string.Empty);
    }

    private TcpClient tcpClient;
    private NetworkStream networkStream;

    private void ProcessMessage(byte[] data)
    {
        if (data == null || data.Length < 7)
        {
            LogMonitorMainThread("[錯誤] 封包長度過短，無法處理", isError: true);
            return;
        }

        try
        {
            byte start = data[0];
            byte format = data[1];
            byte function = data[2];
            byte state = data[3];
            byte length = data[4];
            byte stop = data[^1];

            if (start != 0xDD || stop != 0x77)
            {
                LogMonitorMainThread($"[錯誤] 封包起始/結尾位元不正確: Start=0x{start:X2}, Stop=0x{stop:X2}", isError: true);
                return;
            }

            if (data.Length < 5 + length + 1)
            {
                LogMonitorMainThread($"[錯誤] 資料長度不足以擷取 sourceData (length={length})", isError: true);
                return;
            }

            byte[] sourceData = data[5..(5 + length)];
            float[] sourceBack = new float[length / 4];

            switch (function)
            {
                case 4:
                    length = 0;
                    break;

                case 5:
                    if (length == 5 && sourceData.Length >= 5)
                    {
                        sourceBack[0] = BitConverter.ToSingle(sourceData, 1); // index 1~4 是 float
                    }
                    else
                    {
                        LogMonitorMainThread("[錯誤] Function 5 封包長度錯誤", isError: true);
                    }
                    break;

                case 6:
                    if (sourceData.Length % 4 != 0)
                    {
                        LogMonitorMainThread("[錯誤] Function 6 資料長度不是 4 的倍數", isError: true);
                    }
                    for (int i = 0; i + 4 <= sourceData.Length; i += 4)
                    {
                        sourceBack[i / 4] = BitConverter.ToSingle(sourceData, i);
                    }
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
        catch (Exception ex)
        {
            LogMonitorMainThread($"[錯誤] 處理封包失敗: {ex.Message}", isError: true);
        }
    }


    private async void SendToTargetIp(string ip, int port, string message)
    {
        try
        {
            if (tcpClient == null || !tcpClient.Connected)
            {
                tcpClient?.Close(); // 清掉舊的殘留 client
                tcpClient = new TcpClient();

                var connectTask = tcpClient.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(1000);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask || !tcpClient.Connected)
                {
                    Debug.LogError($"TCP 連線到 {ip}:{port} 超時或失敗");
                    CloseTcpClient();
                    return;
                }

                networkStream = tcpClient.GetStream();
            }

            if (networkStream != null && networkStream.CanWrite)
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                await networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                Debug.Log($"✅ 傳送訊息成功: {message}");
            }
            else
            {
                Debug.LogWarning("⚠️ 無法寫入 NetworkStream");
                CloseTcpClient();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 傳送訊息失敗: {e.Message}");
            CloseTcpClient();
        }
    }


    private void CloseTcpClient()
    {
        try
        {
            if (networkStream != null)
            {
                networkStream.Close();
                networkStream.Dispose();
                networkStream = null;
            }

            if (tcpClient != null)
            {
                if (tcpClient.Connected)
                {
                    tcpClient.Close();
                }
                tcpClient.Dispose();
                tcpClient = null;
            }

            Debug.Log("✅ TCP client 已安全關閉與釋放資源");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 關閉 TCP client 發生錯誤: {e.Message}");
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
        if (!tcpClientdatas.TryGetValue(tcpServerData.portData.ProtocolName, out var tcpClientData))
        {
            LogOnMainThread("❌ 沒有可用的 TCP 客戶端!", isError: true);
            return;
        }

        if (tcpClientData.tcpClient == null || !tcpClientData.IsConnecting || !tcpClientData.tcpClient.Connected)
        {
            tcpClientData.portData.IsConnected = false;
            LogOnMainThread($"⚠️ TCP 客戶端已斷開連接: {tcpClientData.portData.ProtocolName}", isError: true);
            UnityMainThreadDispatcher.Instance().Enqueue(() => tcpClientData.portData.OnUpdate?.Invoke(tcpClientData.portData));
            return;
        }

        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            NetworkStream stream = tcpClientData.tcpClient.GetStream();

            await stream.WriteAsync(buffer, 0, buffer.Length);

            int packetSize = buffer.Length;
            tcpClientData.portData.IsConnected = true;

            if (tcpClientData.portData == this.portData)
            {
                tcpServerData.IncrementSendCount();
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string messageTmp = $"[ {timestamp} ] ✅ 傳送訊息，資料大小: {packetSize}，資料: {message}，發送次數: {tcpServerData.SendCount}";
                LogMonitorMainThread(messageTmp);
            }
        }
        catch (Exception ex)
        {
            LogOnMainThread($"❌ 發送訊息到 TCP 客戶端時出現錯誤: {ex.Message}", isError: true);
            tcpClientData.portData.IsConnected = false;
            UnityMainThreadDispatcher.Instance().Enqueue(() => tcpClientData.portData.OnUpdate?.Invoke(tcpClientData.portData));
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

    /// <summary>
    /// 安全地執行異步操作，捕獲異常並記錄錯誤。
    /// </summary>
    /// <param name="action"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    private async Task SafeAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogOnMainThread($"[AsyncSafe:{context}] 錯誤: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 安全地執行操作，捕獲異常並記錄錯誤。
    /// </summary>
    /// <param name="action"></param>
    /// <param name="context"></param>
    private void Safe(Action action, string context)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LogOnMainThread($"[Safe:{context}] 錯誤: {ex.Message}", isError: true);
        }
    }

    #region 退出應用
    /// <summary>
    /// 
    /// </summary>
    public async void DeInit()
    {
        await ShutdownClientsAsync();
    }

    #endregion
}


