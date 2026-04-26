using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using DevKit;
using iotserver;
using UnityEngine;

/// <summary>
/// UDP 連接器
/// </summary>
public class UdpConnector : NetworkConnectorBase
{
    private readonly ConcurrentDictionary<string, UdpData> udpClients = new();
    private readonly IMainThreadDispatcher dispatcher;

    public UdpConnector(IMainThreadDispatcher dispatcher = null)
    {
        this.dispatcher = dispatcher ?? new UnityDispatcherAdapter();
    }

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

    private bool TryParseIP(string ipString, out IPAddress ipAddress, string context = "")
    {
        ipAddress = null;
        if (string.IsNullOrWhiteSpace(ipString))
            return true; // 允許空IP（廣播模式）

        if (!IPAddress.TryParse(ipString, out ipAddress))
        {
            LogHelper.LogToConsole($"[{context}] {Localization.Instance.GetText(LanguageKeys.Log_InvalidIP)}: {ipString}", isError: true);
            return false;
        }

        return true;
    }

    public override void AddPort(PortData portData)
    {
        if (udpClients.TryGetValue(portData.Key, out var existingServerData))
        {
            if (portData.IsConnected)
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_AlreadyConnected)}");
                return;
            }
            portData.IsConnected = false;
            existingServerData.Dispose();
            udpClients.TryRemove(portData.Key, out _);
        }

        if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "AddPort"))
        {
            portData.IsConnected = false;
            return;
        }

        UdpClient udpClient;
        try
        {
            udpClient = new UdpClient(remotePort);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_PortOccupied)}: {portData.RemotePortDetails.Port}", isError: true);
            throw new InvalidOperationException($"{Localization.Instance.GetText(LanguageKeys.Log_PortOccupied)}: {portData.RemotePortDetails.Port}");
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_StartFailed)}: {ex.Message}", isError: true);
            throw new InvalidOperationException(ex.Message, ex);
        }

        portData.IsConnected = true;

        var newServerData = new UdpData
        {
            portData = portData,
            udpClient = udpClient,
            CancellationTokenSource = new CancellationTokenSource(),
        };

        udpClients[portData.Key] = newServerData;

        ReceiveUdpMessages(newServerData).Forget(ex =>
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ReceiveMessagesUnexpectedError)}: {ex}", isError: true));

        dispatcher.Enqueue(() =>
            SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "UdpConnector.OnUpdate"));
    }

    public override void Connect(PortData portData)
    {
        SafeExecution.Safe(() =>
        {
            if (udpClients.TryGetValue(portData.Key, out var udpData))
            {
                if (portData.IsConnected)
                {
                    LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_AlreadyConnected)}");
                    return;
                }

                if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "Connect"))
                    return;

                try
                {
                    udpData.udpClient?.Close();
                    udpData.udpClient?.Dispose();
                    udpData.udpClient = new UdpClient(remotePort);

                    udpData.CancellationTokenSource?.Cancel();
                    udpData.CancellationTokenSource?.Dispose();
                    udpData.CancellationTokenSource = new CancellationTokenSource();

                    portData.IsConnected = true;

                    ReceiveUdpMessages(udpData).Forget(ex =>
                        LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ReceiveMessagesUnexpectedError)}: {ex}", isError: true));

                    LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ReconnectSuccess)} → {portData.RemotePortDetails.Port}");

                    dispatcher.Enqueue(() =>
                        SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "UdpConnector.OnUpdate"));
                }
                catch (Exception ex)
                {
                    LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ReconnectFailed)}: {ex.Message}", isError: true);
                    portData.IsConnected = false;
                }
            }
            else
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_NotFound)}", isError: true);
            }
        }, "UdpConnector.Connect");
    }

    public override async UniTask Disconnect(PortData portData)
    {
        if (!udpClients.TryGetValue(portData.Key, out var udpData))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_NotFound)}");
            return;
        }

        try
        {
            udpData.CancellationTokenSource?.Cancel();
            await UniTask.Delay(300);

            udpData.udpClient?.Close();
            udpData.udpClient?.Dispose();
            udpData.udpClient = null;

            udpData.CancellationTokenSource?.Dispose();
            udpData.CancellationTokenSource = new CancellationTokenSource();

            portData.IsConnected = false;

            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_Disconnected)}");
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_ReconnectFailed)}: {ex}", isError: true);
        }

        dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
    }

    private async UniTask ReceiveUdpMessages(UdpData udpData)
    {
        if (!TryParsePort(udpData.portData.LocalPortDetails.Port, out int localPort, "ReceiveUdpMessages"))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", udpData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_InvalidPortFormat)}", isError: true);
            return;
        }

        if (!TryParseIP(udpData.portData.TargetIP, out IPAddress targetIP, "ReceiveUdpMessages"))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", udpData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_InvalidIP)}", isError: true);
            return;
        }

        // 切換到 ThreadPool，避免每次 await 都回到 Unity PlayerLoop（每幀 16ms），
        // 讓接收迴圈能以 I/O 速率運行而非受限於幀率。
        await UniTask.SwitchToThreadPool();

        using var sendClient = new UdpClient();
        IPEndPoint sendEndPoint = string.IsNullOrWhiteSpace(udpData.portData.TargetIP)
            ? new IPEndPoint(IPAddress.Broadcast, localPort)
            : new IPEndPoint(targetIP, localPort);

        if (string.IsNullOrWhiteSpace(udpData.portData.TargetIP))
            sendClient.EnableBroadcast = true;

        byte[] buffer = new byte[1024];
        long lastUiUpdateTicks = 0;
        const long UiUpdateIntervalTicks = TimeSpan.TicksPerMillisecond * 100; // UI 最多 10 次/秒

        try
        {
            while (!udpData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                if (udpData.udpClient == null)
                    break;

                var result = await udpData.udpClient.ReceiveAsync()
                    .AsUniTask(useCurrentSynchronizationContext: false)
                    .AttachExternalCancellation(udpData.CancellationTokenSource.Token);

                int messageLength = result.Buffer.Length;

                if (messageLength > buffer.Length)
                    buffer = new byte[messageLength];

                Array.Copy(result.Buffer, buffer, messageLength);

                string message;
                try
                {
                    message = Encoding.UTF8.GetString(buffer, 0, messageLength);
                }
                catch (Exception)
                {
                    message = Encoding.GetEncoding("UTF-8", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetString(buffer, 0, messageLength);
                    LogHelper.LogToConsole($"{LogHelper.Tag("UDP", udpData.portData)} {Localization.Instance.GetText(LanguageKeys.Log_InvalidUTF8)}", isError: true);
                }

                udpData.portData.COMReceived += messageLength;
                udpData.SourceData = message;

                string maskId = udpData.portData.MaskType?.Trim() ?? "OriginalData";
                var def = MaskDefinitionManager.Instance.GetDefinition(maskId)
                       ?? MaskDefinitionManager.Instance.GetDefinition("OriginalData");

                bool anyOutput = false;
                foreach (var rawLine in message.Split('\n'))
                {
                    string line = rawLine.Trim('\r', ' ');
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    RouterLogHelper.LogReceive(udpData.portData, MonitorTargetType.UDP, line);

                    byte[] lineBytes = Encoding.UTF8.GetBytes(line);
                    string output = def != null ? MaskProcessor.Process(def, lineBytes, line) : line;
                    if (string.IsNullOrEmpty(output)) continue;

                    var outBytes = Encoding.UTF8.GetBytes(output.EndsWith("\n") ? output : output + "\n");
                    // UDP send 寫入 OS buffer 後立即返回，不需等待確認
                    sendClient.SendAsync(outBytes, outBytes.Length, sendEndPoint).AsUniTask(false).Forget();
                    udpData.portData.NetReceived += outBytes.Length;
                    RouterLogHelper.LogSend(udpData.portData, MonitorTargetType.UDP, output);
                    anyOutput = true;
                }

                if (anyOutput)
                {
                    long now = DateTime.UtcNow.Ticks;
                    if (now - lastUiUpdateTicks >= UiUpdateIntervalTicks)
                    {
                        lastUiUpdateTicks = now;
                        dispatcher.Enqueue(() => udpData.portData.OnUpdate?.Invoke(udpData.portData));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不記錄
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        // 接收迴圈結束（正常取消或錯誤）
    }

    public override async UniTask RemovePort(PortData portData)
    {
        if (udpClients.TryRemove(portData.Key, out var udpData))
        {
            try
            {
                udpData.CancellationTokenSource?.Cancel();
                await UniTask.Delay(300);

                udpData.Dispose();
                portData.IsConnected = false;

                LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_Removed)}");

                dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_StartFailed)}: {ex}", isError: true);
            }
        }
        else
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} {Localization.Instance.GetText(LanguageKeys.Log_NotFound)}");
        }
    }

    public override async UniTask RestartPort(PortData portData)
    {
        await Disconnect(portData);
        await UniTask.Delay(200);
        AddPort(portData);
    }

    public override async UniTask ShutdownAsync()
    {
        UnityEngine.Debug.Log($"[UDP] Shutting down {udpClients.Count} UDP connections");

        foreach (var udpData in udpClients.Values)
        {
            try { udpData.CancellationTokenSource?.Cancel(); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[UDP] Error cancelling task: {ex.Message}");
            }
        }

        await UniTask.Delay(300);

        foreach (var udpData in udpClients.Values)
        {
            try { udpData.Dispose(); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[UDP] Error disposing: {ex}");
            }
        }

        udpClients.Clear();
        UnityEngine.Debug.Log("[UDP] All UDP connections closed");
    }
}
