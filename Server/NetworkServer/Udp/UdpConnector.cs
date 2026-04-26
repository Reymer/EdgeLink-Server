using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EdgeLink.Mask;
using EdgeLink.NetworkServer.Base;
using EdgeLink.NetworkServer.Base.Models;
using EdgeLink.NetworkServer.Logging;

namespace EdgeLink.NetworkServer.Udp;

public class UdpConnector : NetworkConnectorBase
{
    private readonly ConcurrentDictionary<string, UdpData> _udpClients = new();
    private readonly IMainThreadDispatcher _dispatcher;

    public UdpConnector(IMainThreadDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? DirectDispatcher.Instance;
    }

    private bool TryParsePort(string? portString, out int port, string context = "")
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(portString))
        {
            LogHelper.LogToConsole($"[{context}] Port is empty", isError: true);
            return false;
        }
        if (!int.TryParse(portString, out port))
        {
            LogHelper.LogToConsole($"[{context}] Invalid port format: {portString}", isError: true);
            return false;
        }
        if (port < 1 || port > 65535)
        {
            LogHelper.LogToConsole($"[{context}] Port out of range: {port}", isError: true);
            return false;
        }
        return true;
    }

    private static bool TryParseIP(string? ipString, out IPAddress? ipAddress, string context = "")
    {
        ipAddress = null;
        if (string.IsNullOrWhiteSpace(ipString)) return true;
        if (!IPAddress.TryParse(ipString, out ipAddress))
        {
            LogHelper.LogToConsole($"[{context}] Invalid IP: {ipString}", isError: true);
            return false;
        }
        return true;
    }

    public override void AddPort(PortData portData)
    {
        if (_udpClients.TryGetValue(portData.Key, out var existing))
        {
            if (portData.IsConnected) { LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Already connected"); return; }
            portData.IsConnected = false;
            existing.Dispose();
            _udpClients.TryRemove(portData.Key, out _);
        }

        if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "AddPort"))
        {
            portData.IsConnected = false;
            return;
        }

        UdpClient udpClient;
        try { udpClient = new UdpClient(remotePort); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Port occupied: {portData.RemotePortDetails.Port}", isError: true);
            throw new InvalidOperationException($"Port occupied: {portData.RemotePortDetails.Port}");
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Start failed: {ex.Message}", isError: true);
            throw new InvalidOperationException(ex.Message, ex);
        }

        portData.IsConnected = true;
        var udpData = new UdpData
        {
            portData  = portData,
            udpClient = udpClient,
            CancellationTokenSource = new CancellationTokenSource()
        };
        _udpClients[portData.Key] = udpData;

        _ = ReceiveUdpMessages(udpData).ContinueWith(t =>
        {
            if (t.IsFaulted) LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Receive error: {t.Exception}", isError: true);
        });

        _dispatcher.Enqueue(() => SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "UdpConnector.OnUpdate"));
    }

    public override void Connect(PortData portData)
    {
        SafeExecution.Safe(() =>
        {
            if (!_udpClients.TryGetValue(portData.Key, out var udpData))
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Not found", isError: true);
                return;
            }

            if (portData.IsConnected) { LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Already connected"); return; }
            if (!TryParsePort(portData.RemotePortDetails.Port, out int remotePort, "Connect")) return;

            try
            {
                udpData.udpClient?.Close(); udpData.udpClient?.Dispose();
                udpData.udpClient = new UdpClient(remotePort);

                udpData.CancellationTokenSource?.Cancel();
                udpData.CancellationTokenSource?.Dispose();
                udpData.CancellationTokenSource = new CancellationTokenSource();

                portData.IsConnected = true;
                _ = ReceiveUdpMessages(udpData).ContinueWith(t =>
                {
                    if (t.IsFaulted) LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Receive error: {t.Exception}", isError: true);
                });
                LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Reconnected → {portData.RemotePortDetails.Port}");
                _dispatcher.Enqueue(() => SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "UdpConnector.OnUpdate"));
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Reconnect failed: {ex.Message}", isError: true);
                portData.IsConnected = false;
            }
        }, "UdpConnector.Connect");
    }

    public override async Task Disconnect(PortData portData)
    {
        if (!_udpClients.TryGetValue(portData.Key, out var udpData))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Not found");
            return;
        }
        try
        {
            udpData.CancellationTokenSource?.Cancel();
            await Task.Delay(300);
            udpData.udpClient?.Close(); udpData.udpClient?.Dispose();
            udpData.udpClient = null;
            udpData.CancellationTokenSource?.Dispose();
            udpData.CancellationTokenSource = new CancellationTokenSource();
            portData.IsConnected = false;
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Disconnected");
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Disconnect error: {ex}", isError: true);
        }
        _dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
    }

    public override async Task RemovePort(PortData portData)
    {
        if (!_udpClients.TryRemove(portData.Key, out var udpData))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Not found");
            return;
        }
        try
        {
            udpData.CancellationTokenSource?.Cancel();
            await Task.Delay(300);
            udpData.Dispose();
            portData.IsConnected = false;
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Removed");
            _dispatcher.Enqueue(() => portData.OnUpdate?.Invoke(portData));
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", portData)} Remove error: {ex}", isError: true);
        }
    }

    public override async Task RestartPort(PortData portData)
    {
        await Disconnect(portData);
        await Task.Delay(200);
        AddPort(portData);
    }

    public override async Task ShutdownAsync()
    {
        LogHelper.LogToConsole($"[UDP] Shutting down {_udpClients.Count} UDP connections");
        foreach (var ud in _udpClients.Values)
        {
            try { ud.CancellationTokenSource?.Cancel(); }
            catch (Exception ex) { LogHelper.LogToConsole($"[UDP] Error cancelling: {ex.Message}"); }
        }
        await Task.Delay(300);
        foreach (var ud in _udpClients.Values)
        {
            try { ud.Dispose(); }
            catch (Exception ex) { LogHelper.LogToConsole($"[UDP] Error disposing: {ex}"); }
        }
        _udpClients.Clear();
        LogHelper.LogToConsole("[UDP] All UDP connections closed");
    }

    private async Task ReceiveUdpMessages(UdpData udpData)
    {
        if (!TryParsePort(udpData.portData.LocalPortDetails.Port, out int localPort, "ReceiveUdpMessages"))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", udpData.portData)} Invalid local port", isError: true);
            return;
        }
        if (!TryParseIP(udpData.portData.TargetIP, out IPAddress? targetIP, "ReceiveUdpMessages"))
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", udpData.portData)} Invalid target IP", isError: true);
            return;
        }

        using var sendClient = new UdpClient();
        IPEndPoint sendEndPoint = string.IsNullOrWhiteSpace(udpData.portData.TargetIP)
            ? new IPEndPoint(IPAddress.Broadcast, localPort)
            : new IPEndPoint(targetIP!, localPort);

        if (string.IsNullOrWhiteSpace(udpData.portData.TargetIP))
            sendClient.EnableBroadcast = true;

        byte[] buffer = new byte[1024];
        long lastUiUpdateTicks = 0;
        const long UiUpdateIntervalTicks = TimeSpan.TicksPerMillisecond * 100;
        var token = udpData.CancellationTokenSource.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (udpData.udpClient == null) break;

                var receiveTask  = udpData.udpClient.ReceiveAsync();
                var cancelTask   = Task.Delay(Timeout.Infinite, token);
                var completed    = await Task.WhenAny(receiveTask, cancelTask);
                if (completed == cancelTask) break;

                var result        = receiveTask.Result;
                int messageLength = result.Buffer.Length;

                if (messageLength > buffer.Length) buffer = new byte[messageLength];
                Array.Copy(result.Buffer, buffer, messageLength);

                string message;
                try { message = Encoding.UTF8.GetString(buffer, 0, messageLength); }
                catch
                {
                    message = Encoding.GetEncoding("UTF-8", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetString(buffer, 0, messageLength);
                    LogHelper.LogToConsole($"{LogHelper.Tag("UDP", udpData.portData)} Invalid UTF-8 in data", isError: true);
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
                    string? output   = def != null ? MaskProcessor.Process(def, lineBytes, line) : line;
                    if (string.IsNullOrEmpty(output)) continue;

                    var outBytes = Encoding.UTF8.GetBytes(output.EndsWith("\n") ? output : output + "\n");
                    _ = sendClient.SendAsync(outBytes, outBytes.Length, sendEndPoint);
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
                        _dispatcher.Enqueue(() => udpData.portData.OnUpdate?.Invoke(udpData.portData));
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"{LogHelper.Tag("UDP", udpData.portData)} Receive error: {ex.Message}", isError: true);
        }
    }
}
