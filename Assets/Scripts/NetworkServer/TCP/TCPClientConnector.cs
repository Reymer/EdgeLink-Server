using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DevKit;
using iotserver;

/// <summary>
/// TCP Client 連線管理器
/// </summary>
public class TCPClientConnector : NetworkConnectorBase
{
    private readonly ConcurrentDictionary<string, TCPClientData> tcpClientDatas = new();
    public Action<PortData> OnReconnectSuccess;
    public Action<PortData> OnReconnectFailed;

    private readonly IMainThreadDispatcher dispatcher;
    private readonly TcpClientRetryConfig injectedConfig;

    public TCPClientConnector(IMainThreadDispatcher dispatcher = null, TcpClientRetryConfig retryConfig = null)
    {
        this.dispatcher = dispatcher ?? new UnityDispatcherAdapter();
        this.injectedConfig = retryConfig;
    }

    private TcpClientRetryConfig GetConfig() =>
        injectedConfig ?? NetworkPortManager.Instance.GetTcpClientRetryConfig();

    private bool TryParsePort(string portString, out int port, string context = "")
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(portString))
        {
            LogHelper.LogToConsole($"[{context}] {Localization.Instance.GetText(LanguageKeys.Log_PortEmpty)}", isError: true);
            return false;
        }

        if (!int.TryParse(portString, out port))
        {
            LogHelper.LogToConsole($"[{context}] {Localization.Instance.GetText(LanguageKeys.Log_InvalidPortFormat)}: {portString}", isError: true);
            return false;
        }

        if (port < 1 || port > 65535)
        {
            LogHelper.LogToConsole($"[{context}] {Localization.Instance.GetText(LanguageKeys.Log_PortOutOfRange)}: {port}", isError: true);
            return false;
        }

        return true;
    }

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

        ConnectWithRetryAsync(tcpClientDatas[portData.Key], isFirstConnect: true).Forget();
    }

    public override void Connect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            ConnectWithRetryAsync(clientData, isFirstConnect: true).Forget();
        }
        else
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_NotFound)}", isError: true);
        }
    }

    public override UniTask Disconnect(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            portData.IsConnected = false;
        }
        return UniTask.CompletedTask;
    }

    public override UniTask RemovePort(PortData portData)
    {
        if (tcpClientDatas.TryRemove(portData.Key, out var clientData))
        {
            ResetClientConnection(clientData);
            clientData.Dispose();
            portData.IsConnected = false;
            NetworkMessageRouter.Instance.UnregisterTcpClient(portData.Key);
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_Removed)}");
        }
        return UniTask.CompletedTask;
    }

    public override async UniTask RestartPort(PortData portData)
    {
        if (tcpClientDatas.TryGetValue(portData.Key, out var clientData))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_Restarting)}");
            await Disconnect(portData);
            await ConnectWithRetryAsync(clientData, isFirstConnect: true);
        }
    }

    public TCPClientData GetClientData(PortData portData)
    {
        return tcpClientDatas.TryGetValue(portData.Key, out var clientData) ? clientData : null;
    }

    public override async UniTask ShutdownAsync()
    {
        UnityEngine.Debug.Log($"[TCPClient] Shutting down {tcpClientDatas.Count} TCP clients");

        foreach (var clientData in tcpClientDatas.Values)
        {
            try { clientData.CancellationTokenSource?.Cancel(); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPClient] Error cancelling task: {ex.Message}");
            }
        }

        await UniTask.Delay(100);

        foreach (var clientData in tcpClientDatas.Values)
        {
            try
            {
                ResetClientConnection(clientData);
                clientData.Dispose();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[TCPClient] Error disposing: {ex.Message}");
            }
        }

        tcpClientDatas.Clear();
        UnityEngine.Debug.Log("[TCPClient] All TCP clients closed");
    }

    private async UniTask ConnectWithRetryAsync(TCPClientData clientData, bool isFirstConnect)
    {
        await UniTask.SwitchToThreadPool();
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;
        var cfg = GetConfig();
        int retryCount = 0;
        int maxRetry = isFirstConnect ? cfg.MaxRetryFirst : cfg.MaxRetrySubsequent;

        while (!ShouldStopRetry(clientData, retryCount, maxRetry))
        {
            try
            {
                if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "ConnectWithRetry"))
                {
                    portData.IsConnected = false;
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_InvalidPort)}", isError: true);
                    return;
                }

                clientData.tcpClient?.Close();
                clientData.tcpClient?.Dispose();
                clientData.tcpClient = new TcpClient { NoDelay = true };

                var connectTask = clientData.tcpClient.ConnectAsync(portData.TargetIP, remotePort);
                var timeoutTask = Task.Delay(5000, token);

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    var timedOutClient = clientData.tcpClient;
                    clientData.tcpClient = null;
                    timedOutClient?.Close();
                    timedOutClient?.Dispose();
                    _ = connectTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.None);
                    throw new TimeoutException("TCP connect timeout");
                }

                if (connectTask.IsFaulted)
                    _ = connectTask.Exception;

                if (clientData.tcpClient?.Connected == true)
                {
                    var stream = clientData.tcpClient.GetStream();
                    if (stream == null || !stream.CanWrite)
                        throw new Exception("TCP Stream 不可寫入，視為連線失敗");

                    portData.IsConnected = true;
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_Connected)} → {portData.TargetIP}:{portData.RemotePortDetails.Port}");
                    dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
                    OnReconnectSuccess?.Invoke(portData);
                    clientData.HeartbeatTask = StartHeartbeatAsync(clientData).AsTask();

                    return;
                }
                else
                {
                    throw new Exception("TCP Connect失敗");
                }
            }
            catch (TimeoutException)
            {
                portData.IsConnected = false;
                dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (SocketException)
            {
                portData.IsConnected = false;
                dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (Exception)
            {
                portData.IsConnected = false;
                dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }

            retryCount++;
            await Task.Delay(cfg.InitialDelayMs, token);
        }

        LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_MaxRetry)} ({maxRetry})", isError: true);
        OnReconnectFailed?.Invoke(portData);
    }

    private async UniTask StartHeartbeatAsync(TCPClientData clientData)
    {
        await UniTask.SwitchToThreadPool();
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var cfg = GetConfig();
                await Task.Delay(cfg.HeartbeatIntervalMs, token);

                if (IsSocketDisconnected(clientData.tcpClient))
                {
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_HeartbeatLost)}");
                    portData.IsConnected = false;
                    dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
                    ConnectWithRetryAsync(clientData, isFirstConnect: false).Forget();
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
                    break;
                }
                catch (Exception ex)
                {
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_HeartbeatFailed)}: {ex.Message}", isError: true);
                    portData.IsConnected = false;
                    ConnectWithRetryAsync(clientData, isFirstConnect: false).Forget();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 心跳任務被取消（正常關閉）
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_HeartbeatFailed)}: {ex.Message}", isError: true);
        }
    }

    private bool ShouldStopRetry(TCPClientData clientData, int retryCount, int maxRetry)
    {
        if (clientData.CancellationTokenSource.Token.IsCancellationRequested)
            return true;

        if (maxRetry < 0 || maxRetry == int.MaxValue)
            return false;

        return retryCount >= maxRetry;
    }

    private bool IsSocketDisconnected(TcpClient client)
    {
        try
        {
            if (client == null || !client.Connected) return true;

            Socket socket = client.Client;
            return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch (ObjectDisposedException) { return true; }
        catch (SocketException ex)
        {
            LogHelper.LogToConsole($"[IsSocketDisconnected] SocketException: {ex.SocketErrorCode}");
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[IsSocketDisconnected] 未預期異常: {ex.Message}", isError: true);
            return true;
        }
    }

    private void ResetClientConnection(TCPClientData clientData)
    {
        try
        {
            if (clientData.CancellationTokenSource != null)
            {
                try
                {
                    clientData.CancellationTokenSource.Cancel();
                    clientData.CancellationTokenSource.Dispose();
                }
                catch (ObjectDisposedException) { }
            }

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
            clientData.CancellationTokenSource = new CancellationTokenSource();
            clientData.tcpClient = new TcpClient();
        }
    }
}
