using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static NetworkPortManager;

public class NetworkMessageRouter
{
    private static NetworkMessageRouter instance;
    public static NetworkMessageRouter Instance => instance ??= new NetworkMessageRouter();

    private readonly ConcurrentDictionary<string, TCPClientData> tcpClients = new();
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServers = new();

    private NetworkMessageRouter() { }
    public void RegisterTcpClient(string protocolName, TCPClientData clientData)
    {
        tcpClients[protocolName] = clientData;
    }

    public void UnregisterTcpClient(string protocolName)
    {
        tcpClients.TryRemove(protocolName, out _);
    }

    public void RegisterTcpServer(string protocolName, TCPServerData serverData)
    {
        tcpServers[protocolName] = serverData;
    }

    public void UnregisterTcpServer(string protocolName)
    {
        tcpServers.TryRemove(protocolName, out _);
    }

    public void RouteMessage(object sourceData, byte[] rawBytes, string parsedMessage)
    {
        PortData portData = sourceData switch
        {
            TCPServerData server => server.portData,
            TCPClientData client => client.portData,
            _ => null
        };

        if (portData == null)
        {
            LogHelper.LogToConsole("[Router] 錯誤：找不到 PortData！", isError: true);
            return;
        }

        string maskType = portData.MaskType?.Trim() ?? "";

        if (string.IsNullOrEmpty(maskType))
        {
            LogHelper.LogToConsole("[Router] 未設定 MaskType，無法處理", isError: true);
            return;
        }

        switch (maskType)
        {
            case "Robot to 10":
                HandleRobotTo10(portData, rawBytes);
                break;
            case "Robot to 16":
                HandleRobotTo16(portData, parsedMessage);
                break;
            case "original data":
                HandleOriginalData(sourceData, parsedMessage);
                break;
            default:
                LogHelper.LogToConsole($"[Router] 未識別的 MaskType: {maskType}", isError: true);
                break;
        }
    }
    private void HandleRobotTo10(PortData portData, byte[] rawBytes)
    {
        if (rawBytes == null || rawBytes.Length < 7)
        {
            LogHelper.LogToMonitor("[RobotTo10] 錯誤：收到的 bytes 長度過短");
            return;
        }

        string hex = BitConverter.ToString(rawBytes).Replace("-", " ");

        if (rawBytes[0] == 0xDD && rawBytes[^1] == 0x77)
        {
            ProcessRobotTo10Message(rawBytes);

            if (MonitorManager.Instance.IsMonitoring(portData))
            {
                LogHelper.LogToMonitor($"[監控] RobotTo10 正常封包: {hex}");
            }
        }
        else
        {
            LogHelper.LogToMonitor($"[RobotTo10] 格式錯誤（需 0xDD~0x77），收到: {hex}");

            if (MonitorManager.Instance.IsMonitoring(portData))
            {
                LogHelper.LogToMonitor($"[監控] RobotTo10 格式錯誤封包: {hex}");
            }
        }
    }


    private void ProcessRobotTo10Message(byte[] data)
    {
        try
        {
            byte function = data[2];
            byte state = data[3];
            byte length = data[4];
            byte[] payload = data.Skip(5).Take(length).ToArray();

            string message = function switch
            {
                4 => $"{function}:{state}:{length}",
                5 => $"{function}:{state}:{length}:{payload[0]}:{BitConverter.ToSingle(payload, 1)}",
                6 => $"{function}:{state}:{length}:{string.Join(":", Enumerable.Range(0, payload.Length / 4).Select(i => BitConverter.ToSingle(payload, i * 4)))}",
                9 => $"{function}:{state}:{length}:{payload[0]}",
                _ => $"{function}:{state}:{length}"
            };

            SendUdp("192.168.1.3", 12000, message); // 可參數化
        }
        catch (Exception ex)
        {
            LogHelper.LogToMonitor($"[錯誤] 處理 RobotTo10 封包失敗: {ex.Message}");
        }
    }
    private void HandleRobotTo16(PortData portData, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var parts = message.Split(':');
            if (parts.Length < 3)
            {
                LogHelper.LogToMonitor($"[Router] RobotTo16 格式錯誤：{message}");
                return;
            }

            int format = int.Parse(parts[0]);
            int function = int.Parse(parts[1]);
            int length = int.Parse(parts[2]);

            string payload = function switch
            {
                1 => $"{ConvertToIEEE754(parts[3])}:{ConvertToIEEE754(parts[4])}",
                2 => ConvertToIEEE754(parts[3]),
                3 or 7 or 8 or 10 => "0x" + int.Parse(parts[3]).ToString("X2"),
                _ => string.Empty
            };

            var builtPacket = BuildRobotTo16Packet(format, function, length, payload);
            ForwardToClient(portData.ProtocolName, builtPacket);
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] 處理 RobotTo16 發生錯誤: {ex.Message}", isError: true);
        }
    }

    private void HandleOriginalData(object sourceData, string message)
    {
        try
        {
            PortData portData = sourceData switch
            {
                TCPServerData server => server.portData,
                TCPClientData client => client.portData,
                _ => null
            };

            if (portData == null)
            {
                LogHelper.LogToConsole("[Router] 未知來源，無法處理。", isError: true);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(message + "\n");

            if (sourceData is TCPServerData serverData)
            {
                ForwardToClient(portData.ProtocolName, bytes);

                if (MonitorManager.Instance.IsMonitoring(portData))
                {
                    int id = MonitorCounter.Next();
                    string remoteIP = serverData?.RemoteEndPoint?.ToString() ?? "未知IP";

                    LogHelper.LogToMonitor($"[監控 #{id}] 收到 (TCP Server, {remoteIP}): {message}");
                }
            }
            else if (sourceData is TCPClientData clientData)
            {
                if (MonitorManager.Instance.IsMonitoring(portData))
                {
                    int id = MonitorCounter.Next();
                    string remoteIP = clientData?.tcpClient?.Client?.RemoteEndPoint?.ToString() ?? "未知IP";

                    LogHelper.LogToMonitor($"[監控 #{id}] 收到 (TCP Client, {remoteIP}): {message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] 處理原始資料發生錯誤: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 轉到特定客戶端
    /// </summary>
    /// <param name="protocolName"></param>
    /// <param name="data"></param>
    private async void ForwardToClient(string protocolName, byte[] data)
    {
        if (string.IsNullOrEmpty(protocolName)) return;

        if (tcpClients.TryGetValue(protocolName, out var client) && client?.tcpClient?.Connected == true)
        {
            try
            {
                await client.tcpClient.GetStream().WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"[Router] 轉送到 TCP Client 失敗: {ex.Message}", isError: true);

                try
                {
                    client.tcpClient?.Close();
                    client.tcpClient?.Dispose();
                }
                catch { }

                client.tcpClient = null;
                client.portData.IsConnected = false;

                UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                    SafeExecution.Safe(() => client.portData.OnUpdate?.Invoke(client.portData)));

                LogHelper.LogToConsole($"TCP Client [{protocolName}] 已偵測到斷線，已自動關閉連線。");
            }
        }
    }

    private void SendUdp(string ip, int port, string message)
    {
        try
        {
            using var udp = new System.Net.Sockets.UdpClient();
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            udp.SendAsync(bytes, bytes.Length, ip, port);
            LogHelper.LogToMonitor($"[Router] UDP發送成功 {ip}:{port} -> {message}");
        }
        catch (Exception ex)
        {
            LogHelper.LogToMonitor($"[Router] UDP發送失敗: {ex.Message}");
        }
    } 

    private string ConvertToIEEE754(string decimalStr)
    {
        if (!float.TryParse(decimalStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"無法轉換成浮點數：{decimalStr}");

        return string.Join(":", BitConverter.GetBytes(value).Select(b => $"0x{b:X2}"));
    }

    /// <summary>
    /// 建立 RobotTo16 封包
    /// </summary>
    /// <param name="format"></param>
    /// <param name="function"></param>
    /// <param name="length"></param>
    /// <param name="payload"></param>
    /// <returns></returns>
    private byte[] BuildRobotTo16Packet(int format, int function, int length, string payload)
    {
        List<byte> packet = new() { 0xDD, (byte)format, (byte)function, (byte)length };

        if (!string.IsNullOrEmpty(payload))
        {
            foreach (var hex in payload.Split(':'))
                if (!string.IsNullOrEmpty(hex))
                    packet.Add(Convert.ToByte(hex[2..], 16));
        }

        var crc = CalculateCrc16(packet.Skip(1).ToArray());
        packet.Add((byte)(crc & 0xFF));
        packet.Add((byte)((crc >> 8) & 0xFF));
        packet.Add(0x77);

        return packet.ToArray();
    }

    /// <summary>
    /// 計算 CRC16 校驗碼
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private ushort CalculateCrc16(IEnumerable<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }
        return crc;
    }
}
