using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 網路訊息路由器
/// </summary>
public class NetworkMessageRouter
{
    private static NetworkMessageRouter instance;
    public static NetworkMessageRouter Instance => instance ??= new NetworkMessageRouter();

    //private readonly ConcurrentDictionary<string, TCPClientData> tcpClients = new();
    private readonly ConcurrentDictionary<string, TCPClientData> tcpClients = new();
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServers = new();

    private NetworkMessageRouter() { }

    /// <summary>
    /// 註冊 TCP Client
    /// </summary>
    /// <param name="protocolName"></param>
    /// <param name="clientData"></param>
    public void RegisterTcpClient(TCPClientData clientData)
    {
        string key = GetClientKey(clientData.portData);

        if (tcpClients.ContainsKey(key))
        {
            Debug.LogWarning($"[Router] 已註冊 TCP Client: {key}");
            return;
        }
        tcpClients[key] = clientData;
        Debug.Log($"[Router] 註冊 TCP Client 成功: {key}");
    }

    private string GetClientKey(PortData data)
    {
        return $"{data.ProtocolName}_{data.TargetIP}_{data.RemotePortDetails.Port}";
    }

    /// <summary>
    /// 註銷 TCP Client
    /// </summary>
    /// <param name="protocolName"></param>
    public void UnregisterTcpClient(TCPClientData clientData)
    {
        string key = GetClientKey(clientData.portData);
        tcpClients.TryRemove(key, out _);
    }

    /// <summary>
    /// 註冊 TCP Server
    /// </summary>
    /// <param name="protocolName"></param>
    /// <param name="serverData"></param>
    public void RegisterTcpServer(string protocolName, TCPServerData serverData)
    {
        tcpServers[protocolName] = serverData;
    }

    /// <summary>
    /// 註銷 TCP Server
    /// </summary>
    /// <param name="protocolName"></param>
    public void UnregisterTcpServer(string protocolName)
    {
        tcpServers.TryRemove(protocolName, out _);
    }

    /// <summary>
    /// 路由 TCP Server 收到的封包
    /// </summary>
    /// <param name="serverData"></param>
    /// <param name="rawBytes"></param>
    /// <param name="parsedMessage"></param>
    public void RouteMessage(TCPServerData serverData, byte[] rawBytes, string parsedMessage)
    {
        var portData = serverData.portData;
        RouterLogHelper.LogReceive(serverData.portData, MonitorTargetType.TCPServer, parsedMessage);
        string maskType = portData.MaskType?.Trim() ?? "";

        switch (maskType)
        {
            case "Robot to 10":
                HandleRobotTo10(portData, rawBytes);
                break;
            case "Robot to 16":
                HandleRobotTo16(portData, parsedMessage);
                break;
            case "original data":
                HandleOriginalData(portData, parsedMessage);
                break;
            default:
                LogHelper.LogToConsole($"[Router] 未識別的 MaskType: {maskType}", isError: true);
                break;
        }
    }

    /// <summary>
    /// 處理 RobotTo10 封包
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="rawBytes"></param>
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

            if (MonitorManager.Instance.IsMonitoring(portData, MonitorTargetType.TCPServer))
            {
                LogHelper.LogToMonitor($"[監控] RobotTo10 正常封包: {hex}");
            }
        }
        else
        {
            LogHelper.LogToMonitor($"[RobotTo10] 格式錯誤（需 0xDD~0x77），收到: {hex}");

            if (MonitorManager.Instance.IsMonitoring(portData, MonitorTargetType.TCPServer))
            {
                LogHelper.LogToMonitor($"[監控] RobotTo10 格式錯誤封包: {hex}");
            }
        }
    }

    /// <summary>
    /// 處理 RobotTo10 封包內容
    /// </summary>
    /// <param name="data"></param>
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

    /// <summary>
    /// 處理 RobotTo16 封包
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="message"></param>
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

    /// <summary>
    /// 處理原始資料
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="message"></param>
    private void HandleOriginalData(PortData portData, string message)
    {
        try
        {
            if (portData == null)
            {
                LogHelper.LogToConsole("[Router] 未知來源，無法處理。", isError: true);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(message + "\n");

            ForwardToClient(portData.ProtocolName, bytes);
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] 處理原始資料發生錯誤: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 將封包轉發到 TCP Client
    /// </summary>
    /// <param name="protocolName"></param>
    /// <param name="data"></param>
    private async void ForwardToClient(string protocolName, byte[] data)
    {
        if (string.IsNullOrEmpty(protocolName)) return;

        var matchingClients = tcpClients
            .Where(kv => kv.Key.StartsWith(protocolName + "_"))
            .Select(kv => kv.Value)
            .ToList();

        if (matchingClients.Count == 0) return;

        string parsedMessage = Encoding.UTF8.GetString(data);
        foreach (var client in matchingClients)
        {
            if (client?.tcpClient?.Connected == true)
            {
                try
                {
                    await client.tcpClient.GetStream().WriteAsync(data, 0, data.Length);
                    RouterLogHelper.LogSend(client.portData, MonitorTargetType.TCPClient, parsedMessage);
                }
                catch (Exception ex)
                {
                    //LogHelper.LogToConsole($"[Router] 轉送到 TCP Client 失敗: {ex.Message}", isError: true);

                    try { client.tcpClient?.Close(); client.tcpClient?.Dispose(); } catch { }

                    client.tcpClient = null;
                    client.portData.IsConnected = false;

                    UnregisterTcpClient(client);  // ✅ 改為用完整 clientData 做移除

                    UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                        SafeExecution.Safe(() => client.portData.OnUpdate?.Invoke(client.portData)));
                }
            }
        }
    }



    /// <summary>
    /// 取得 TCP Client 資料
    /// </summary>
    /// <param name="protocolName"></param>
    /// <returns></returns>
    public List<TCPClientData> GetTcpClients(string protocolName)
    {
        return tcpClients
            .Where(kv => kv.Key.StartsWith(protocolName + "_"))
            .Select(kv => kv.Value)
            .ToList();
    }



    /// <summary>
    /// 發送 UDP 封包
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <param name="message"></param>
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

    /// <summary>
    /// 將十進制字串轉換為 IEEE754 格式的十六進制字串
    /// </summary>
    /// <param name="decimalStr"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
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
