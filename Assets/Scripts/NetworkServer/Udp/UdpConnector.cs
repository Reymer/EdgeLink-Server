using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static NetworkPortManager;
using System.Collections.Generic;
using System;

public class UdpConnector
{
    private readonly ConcurrentDictionary<string, UdpData> udpClients = new();

    /// <summary>
    /// 添加端口
    /// </summary>
    /// <param name="portData"></param>
    public void AddPort(PortData portData)
    {
        SafeExecution.Safe(() =>
        {
            if (udpClients.TryGetValue(portData.ProtocolName, out var existingServerData))
            {
                if (portData.IsConnected)
                {
                    LogHelper.LogToConsole($"端口 {portData.RemotePortDetails.Port} 已經存在 UDP 伺服器，正在連接中。");
                    return;
                }

                SafeExecution.Safe(() => existingServerData.CancellationTokenSource?.Cancel(), "UdpConnector.CancelOldToken");
                SafeExecution.Safe(() => existingServerData.udpClient?.Dispose(), "UdpConnector.DisposeOldUdp");
                existingServerData.udpClient = null;
                portData.IsConnected = false;
                SafeExecution.Safe(() => Task.Delay(100).Wait(), "UdpConnector.WaitDispose");
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
                };

                udpClients[portData.ProtocolName] = newServerData;

                LogHelper.LogToConsole($"在端口 {portData.RemotePortDetails.Port} 上啟動了 UDP 伺服器端。");

                Task.Run(() => SafeExecution.SafeAsync(() => ReceiveUdpMessages(newServerData), "UdpConnector.ReceiveUdpMessages"));

                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    SafeExecution.Safe(() => portData.OnUpdate?.Invoke(portData), "UdpConnector.OnUpdate"));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                LogHelper.LogToConsole($"端口 {portData.RemotePortDetails.Port} 已經被使用。", isError: true);
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"初始化端口 {portData.RemotePortDetails.Port} 的 UDP 監聽器時發生錯誤: {ex.Message}", isError: true);
            }

        }, "UdpConnector.AddPort");
    }

    /// <summary>
    /// 主動斷開連接
    /// </summary>
    /// <param name="portData"></param>
    public async void Disconnect(PortData portData)
    {
        if (!udpClients.ContainsKey(portData.ProtocolName))
        {
            LogHelper.LogToConsole($"未找到 UDP 伺服器，端口 {portData.RemotePortDetails.Port}");
            return;
        }

        var udpData = udpClients[portData.ProtocolName];

        if (portData.IsConnected && udpData.udpClient != null)
        {
            try
            {
                udpData.CancellationTokenSource.Cancel();
                portData.IsConnected = false;

                await Task.Delay(100);
                udpData.Dispose();

                LogHelper.LogToConsole($"已主動斷開 UDP 連接，端口 {portData.RemotePortDetails.Port}");
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"主動斷開連接失敗，端口 {portData.RemotePortDetails.Port}: {ex.Message}");
            }
            finally
            {
                udpData.CancellationTokenSource = new CancellationTokenSource();
            }
        }
        UnityMainThreadDispatcher.Instance().Enqueue(() => portData.OnUpdate?.Invoke(portData));
    }

    /// <summary>
    /// 接收 UDP 訊息
    /// </summary>
    /// <param name="udpData"></param>
    /// <returns></returns>
    private async Task ReceiveUdpMessages(UdpData udpData)
    {
        using var sendClient = new UdpClient();
        IPEndPoint sendEndPoint = string.IsNullOrWhiteSpace(udpData.portData.TargetIP)
            ? new IPEndPoint(IPAddress.Broadcast, int.Parse(udpData.portData.LocalPortDetails.Port))
            : new IPEndPoint(IPAddress.Parse(udpData.portData.TargetIP), int.Parse(udpData.portData.LocalPortDetails.Port));

        if (string.IsNullOrWhiteSpace(udpData.portData.TargetIP))
            sendClient.EnableBroadcast = true;

        byte[] buffer = new byte[1024];

        try
        {
            while (!udpData.CancellationTokenSource.Token.IsCancellationRequested)
            {
                var result = await SafeExecution.WithCancellation(udpData.udpClient.ReceiveAsync(), udpData.CancellationTokenSource.Token);
                int messageLength = result.Buffer.Length;

                if (messageLength > buffer.Length)
                    buffer = new byte[messageLength];

                Array.Copy(result.Buffer, buffer, messageLength);
                string message = Encoding.UTF8.GetString(buffer, 0, messageLength);

                udpData.portData.COMReceived += messageLength;
                udpData.SourceData = message;

                LogHelper.LogToMonitor($"名稱: {udpData.portData.ProtocolName}。收到來自: {result.RemoteEndPoint} 的訊息，大小: {messageLength} bytes, 訊息: {message}");

                await sendClient.SendAsync(result.Buffer, messageLength, sendEndPoint);
                udpData.portData.NetReceived += messageLength;

                LogHelper.LogToMonitor($"名稱: {udpData.portData.ProtocolName}。傳送到: {sendEndPoint}, 大小: {messageLength} bytes, 訊息: {message}");
                UnityMainThreadDispatcher.Instance().Enqueue(() => udpData.portData.OnUpdate?.Invoke(udpData.portData));
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"接收 UDP 訊息時發生錯誤: {ex.Message}");
        }

        udpData.Dispose();
        Debug.Log($"UDP 接收器已在端口 {udpData.portData.RemotePortDetails.Port} 上關閉。");
    }

    /// <summary>
    /// 關閉所有 UDP 連接
    /// </summary>
    /// <returns></returns>
    public async Task ShutdownAsync()
    {
        var tasks = new List<Task>();
        foreach (var udpData in udpClients.Values)
        {
            tasks.Add(Task.Run(() => udpData.Dispose()));
        }

        await Task.WhenAll(tasks);
        udpClients.Clear();
    }
}