using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using iotserver;
using UnityEngine;

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
            ConnectWithRetryAsync(clientData, isFirstConnect: true).Forget();
        }
        // 若 port 已存在（已在連線或重連中），不重複啟動。
        // 需要強制重連請改呼叫 Connect()。
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

                    ConfigureKeepAlive(clientData.tcpClient.Client);

                    // Cancel any stale background loops (ProcessSerialQueueAsync / ProcessPollingAsync /
                    // StartReceiveAsync / StartHeartbeatAsync) left over from a previous connection.
                    // Each of those tasks captures the CTS token at the time they start, so replacing
                    // the CTS here causes the old token to become cancelled and the old loops to exit
                    // gracefully, while new tasks will pick up the fresh token.
                    var staleCts = clientData.CancellationTokenSource;
                    clientData.CancellationTokenSource = new CancellationTokenSource();
                    try { staleCts.Cancel(); }  catch (ObjectDisposedException) { }
                    try { staleCts.Dispose(); } catch (ObjectDisposedException) { }

                    // 丟棄前一個 session 殘留在佇列中的舊請求，避免重連後傳送過時資料給裝置。
                    clientData.RequestQueue.Clear();

                    portData.IsConnected = true;
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_Connected)} → {portData.TargetIP}:{portData.RemotePortDetails.Port}");
                    dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
                    OnReconnectSuccess?.Invoke(portData);
                    clientData.HeartbeatTask = StartHeartbeatAsync(clientData).AsTask();

                    StartReceiveAsync(clientData, stream).Forget(ex =>
                        LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} [Receive] {ex.Message}", isError: true));

                    bool isConcurrent = string.Equals(portData.RequestMode, "concurrent", StringComparison.OrdinalIgnoreCase);
                    bool isPolling    = string.Equals(portData.RequestMode, "polling",    StringComparison.OrdinalIgnoreCase);
                    if (!isConcurrent)
                    {
                        if (isPolling)
                            ProcessPollingAsync(clientData, stream).Forget(ex =>
                                LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} [Polling] {ex.Message}", isError: true));
                        else
                            ProcessSerialQueueAsync(clientData, stream).Forget(ex =>
                                LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} [SerialQueue] {ex.Message}", isError: true));
                    }

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
                if (retryCount == 0)
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ConnectTimeout)} → {portData.TargetIP}:{portData.RemotePortDetails.Port}", isError: true);
                dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (SocketException ex)
            {
                portData.IsConnected = false;
                if (retryCount == 0)
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ConnectFailed)} ({ex.SocketErrorCode}) → {portData.TargetIP}:{portData.RemotePortDetails.Port}", isError: true);
                dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (Exception ex)
            {
                portData.IsConnected = false;
                if (retryCount == 0)
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ConnectFailed)} ({ex.Message}) → {portData.TargetIP}:{portData.RemotePortDetails.Port}", isError: true);
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

    private static void ConfigureKeepAlive(Socket socket)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            // Windows IOControl：idle 10s 後開始探測，每 1s 探測一次
            byte[] inValue = new byte[12];
            BitConverter.GetBytes(1u).CopyTo(inValue, 0);
            BitConverter.GetBytes(10_000u).CopyTo(inValue, 4);
            BitConverter.GetBytes(1_000u).CopyTo(inValue, 8);
            socket.IOControl(IOControlCode.KeepAliveValues, inValue, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TCPClient] ConfigureKeepAlive failed: {ex.Message}");
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

    /// <summary>
    /// 持續讀取設備回傳的資料，交給 Router 做反向路由
    /// </summary>
    private async UniTask StartReceiveAsync(TCPClientData clientData, System.Net.Sockets.NetworkStream stream)
    {
        await UniTask.SwitchToThreadPool();
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;
        byte[] buffer = new byte[2048];
        var lineBuffer = new System.Text.StringBuilder();
        const int MaxBufferSize = 1024 * 1024;

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0) break;

                string chunk;
                try { chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead); }
                catch { chunk = System.Text.Encoding.GetEncoding("UTF-8", System.Text.EncoderFallback.ReplacementFallback, System.Text.DecoderFallback.ReplacementFallback).GetString(buffer, 0, bytesRead); }

                if (lineBuffer.Length + chunk.Length > MaxBufferSize)
                    lineBuffer.Clear();

                lineBuffer.Append(chunk);
                string current = lineBuffer.ToString();
                int lastNewline = current.LastIndexOf('\n');
                if (lastNewline < 0) continue;

                string processable = current[..lastNewline];
                string remaining = current[(lastNewline + 1)..];
                lineBuffer.Clear();
                lineBuffer.Append(remaining);

                foreach (var rawLine in processable.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    byte[] lineBytes = System.Text.Encoding.UTF8.GetBytes(line);
                    await NetworkMessageRouter.Instance.RouteResponseAsync(clientData, lineBytes, line);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (System.IO.IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            // 設備斷線：若有等待中的 serial 請求，釋放 signal 避免永遠等待
            clientData.ResponseSignal?.TrySetCanceled();
            LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} 設備連線中斷（receive loop 結束）");
        }
    }

    /// <summary>
    /// Polling 模式：永遠只保留最新一筆，收到回應（或 timeout）後立即處理下一筆
    /// </summary>
    private async UniTask ProcessPollingAsync(TCPClientData clientData, System.Net.Sockets.NetworkStream stream)
    {
        await UniTask.SwitchToThreadPool();
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;
        const int TimeoutMs = 3000;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await clientData.PollTrigger.WaitAsync(token);

                var slot = System.Threading.Interlocked.Exchange(ref clientData.LatestPollRequest, null);
                if (slot == null) continue;

                clientData.CurrentPendingRequester = slot.Requester;
                var signal = new System.Threading.Tasks.TaskCompletionSource<bool>();
                clientData.ResponseSignal = signal;

                await clientData.DeviceWriteLock.WaitAsync(token);
                try
                {
                    await stream.WriteAsync(slot.Data, 0, slot.Data.Length, token);
                    RouterLogHelper.LogSend(portData, MonitorTargetType.TCPClient,
                        System.Text.Encoding.UTF8.GetString(slot.Data));
                }
                catch (Exception ex)
                {
                    clientData.CurrentPendingRequester = null;
                    clientData.ResponseSignal = null;
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} [Polling] 送出失敗: {ex.Message}", isError: true);
                    clientData.DeviceWriteLock.Release();
                    continue;
                }
                clientData.DeviceWriteLock.Release();

                await System.Threading.Tasks.Task.WhenAny(
                    signal.Task,
                    System.Threading.Tasks.Task.Delay(TimeoutMs, token));

                clientData.CurrentPendingRequester = null;
                clientData.ResponseSignal = null;
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Serial 模式的請求佇列處理器：依序送出請求，等設備回應後再送下一筆
    /// </summary>
    private async UniTask ProcessSerialQueueAsync(TCPClientData clientData, System.Net.Sockets.NetworkStream stream)
    {
        await UniTask.SwitchToThreadPool();
        var portData = clientData.portData;
        var token = clientData.CancellationTokenSource.Token;
        const int TimeoutMs = 5000;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var (requester, data) = await clientData.RequestQueue.DequeueAsync(token);
                if (data == null) break;

                // 先設定 signal，再送出資料，避免設備回應比 signal 設定更快而 miss
                clientData.CurrentPendingRequester = requester;
                var signal = new System.Threading.Tasks.TaskCompletionSource<bool>();
                clientData.ResponseSignal = signal;

                // 取得裝置寫入鎖（若 token 被取消，OperationCanceledException 傳至外層 catch 正常退出）
                await clientData.DeviceWriteLock.WaitAsync(token);
                try
                {
                    await stream.WriteAsync(data, 0, data.Length, token);
                    RouterLogHelper.LogSend(portData, MonitorTargetType.TCPClient,
                        System.Text.Encoding.UTF8.GetString(data));
                }
                catch (Exception ex)
                {
                    clientData.CurrentPendingRequester = null;
                    clientData.ResponseSignal = null;
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} [SerialQueue] 送出失敗: {ex.Message}", isError: true);
                    clientData.DeviceWriteLock.Release();
                    continue;
                }
                clientData.DeviceWriteLock.Release();

                // 等設備回應，或 timeout
                var completed = await System.Threading.Tasks.Task.WhenAny(
                    signal.Task,
                    System.Threading.Tasks.Task.Delay(TimeoutMs, token));

                // 裝置斷線：StartReceiveAsync 呼叫 TrySetCanceled → 結束迴圈
                if (completed == signal.Task && signal.Task.IsCanceled)
                    break;

                if (completed != signal.Task)
                {
                    // Timeout：清除 pending，繼續下一筆
                    clientData.CurrentPendingRequester = null;
                    clientData.ResponseSignal = null;
                    LogHelper.LogToConsole($"{LogHelper.Tag("TCP Client", portData)} [SerialQueue] 等待回應逾時（{TimeoutMs}ms）", isError: true);
                }
            }
        }
        catch (OperationCanceledException) { }
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
