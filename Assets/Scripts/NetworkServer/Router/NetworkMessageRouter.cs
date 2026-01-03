using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// 網路訊息路由器
/// </summary>
public class NetworkMessageRouter
{
    private static NetworkMessageRouter instance;
    public static NetworkMessageRouter Instance => instance ??= new NetworkMessageRouter();

    private readonly ConcurrentDictionary<string, TCPClientData> tcpClients = new();
    private readonly ConcurrentDictionary<string, TCPServerData> tcpServers = new();

    private NetworkMessageRouter() { }

    /// <summary>
    /// 註冊 TCP Client
    /// </summary>
    /// <param name="protocolKey"></param>
    /// <param name="clientData"></param>
    public void RegisterTcpClient(string protocolKey, TCPClientData clientData)
    {
        tcpClients[protocolKey] = clientData;
    }

    /// <summary>
    /// 註銷 TCP Client
    /// </summary>
    /// <param name="protocolName"></param>
    public void UnregisterTcpClient(string protocolKey)
    {
        tcpClients.TryRemove(protocolKey, out _);
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
                _ = HandleRobotTo16(portData, parsedMessage);
                break;
            case "original data":
                _ = HandleOriginalData(portData, parsedMessage);
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
            ProcessRobotTo10Message(portData, rawBytes);

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
    /// <param name="portData"></param>
    /// <param name="data"></param>
    private void ProcessRobotTo10Message(PortData portData, byte[] data)
    {
        try
        {
            // 陣列邊界檢查
            if (data == null || data.Length < 5)
            {
                LogHelper.LogToMonitor($"[RobotTo10] 封包長度不足：需要至少5個字節，實際 {data?.Length ?? 0}");
                return;
            }

            byte function = data[2];
            byte state = data[3];
            byte length = data[4];

            // 檢查 payload 長度是否足夠
            if (data.Length < 5 + length)
            {
                LogHelper.LogToMonitor($"[RobotTo10] Payload 長度不足：需要 {5 + length} 字節，實際 {data.Length}");
                return;
            }

            byte[] payload = data.Skip(5).Take(length).ToArray();

            string message = null;

            // ✅ 修復：添加陣列邊界檢查
            switch (function)
            {
                case 4:
                    message = $"{function}:{state}:{length}";
                    break;

                case 5:
                    // 檢查：需要至少 5 字節（1 byte + 4 bytes for float）
                    if (payload.Length >= 5)
                    {
                        message = $"{function}:{state}:{length}:{payload[0]}:{BitConverter.ToSingle(payload, 1)}";
                    }
                    else
                    {
                        LogHelper.LogToMonitor($"[RobotTo10] Function 5 Payload 長度不足：需要至少 5 字節，實際 {payload.Length}");
                    }
                    break;

                case 6:
                    // 檢查：長度必須是 4 的倍數
                    if (payload.Length > 0 && payload.Length % 4 == 0)
                    {
                        message = $"{function}:{state}:{length}:{string.Join(":", Enumerable.Range(0, payload.Length / 4).Select(i => BitConverter.ToSingle(payload, i * 4)))}";
                    }
                    else
                    {
                        LogHelper.LogToMonitor($"[RobotTo10] Function 6 Payload 長度必須是 4 的倍數，實際 {payload.Length}");
                    }
                    break;

                case 9:
                    if (payload.Length >= 1)
                    {
                        message = $"{function}:{state}:{length}:{payload[0]}";
                    }
                    else
                    {
                        LogHelper.LogToMonitor($"[RobotTo10] Function 9 Payload 長度不足：需要至少 1 字節，實際 {payload.Length}");
                    }
                    break;

                default:
                    message = $"{function}:{state}:{length}";
                    break;
            }

            if (message != null)
            {
                // ✅ 修復：使用配置的轉發目標，而非硬編碼
                string targetIP = string.IsNullOrWhiteSpace(portData.ForwardTargetIP) ? "192.168.1.3" : portData.ForwardTargetIP;
                int targetPort = portData.ForwardTargetPort > 0 ? portData.ForwardTargetPort : 12000;

                SendUdp(targetIP, targetPort, message);
            }
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
    private async Task HandleRobotTo16(PortData portData, string message)
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

            // 安全解析整數值
            if (!int.TryParse(parts[0], out int format))
            {
                LogHelper.LogToMonitor($"[Router] RobotTo16 格式欄位無效: {parts[0]}");
                return;
            }

            if (!int.TryParse(parts[1], out int function))
            {
                LogHelper.LogToMonitor($"[Router] RobotTo16 功能欄位無效: {parts[1]}");
                return;
            }

            if (!int.TryParse(parts[2], out int length))
            {
                LogHelper.LogToMonitor($"[Router] RobotTo16 長度欄位無效: {parts[2]}");
                return;
            }

            // 根據 function 檢查所需參數數量
            string payload;
            switch (function)
            {
                case 1:
                    if (parts.Length < 5)
                    {
                        LogHelper.LogToMonitor($"[Router] RobotTo16 Function 1 需要至少5個參數，實際 {parts.Length}");
                        return;
                    }
                    payload = $"{ConvertToIEEE754(parts[3])}:{ConvertToIEEE754(parts[4])}";
                    break;
                case 2:
                    if (parts.Length < 4)
                    {
                        LogHelper.LogToMonitor($"[Router] RobotTo16 Function 2 需要至少4個參數，實際 {parts.Length}");
                        return;
                    }
                    payload = ConvertToIEEE754(parts[3]);
                    break;
                case 3:
                case 7:
                case 8:
                case 10:
                    if (parts.Length < 4)
                    {
                        LogHelper.LogToMonitor($"[Router] RobotTo16 Function {function} 需要至少4個參數，實際 {parts.Length}");
                        return;
                    }
                    if (!int.TryParse(parts[3], out int paramValue))
                    {
                        LogHelper.LogToMonitor($"[Router] RobotTo16 參數無效: {parts[3]}");
                        return;
                    }
                    payload = "0x" + paramValue.ToString("X2");
                    break;
                default:
                    payload = string.Empty;
                    break;
            }

            var builtPacket = BuildRobotTo16Packet(format, function, length, payload);
            await ForwardToClient(portData.ProtocolName, builtPacket);
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
    private async Task HandleOriginalData(PortData portData, string message)
    {
        try
        {
            if (portData == null)
            {
                LogHelper.LogToConsole("[Router] 未知來源，無法處理。", isError: true);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(message + "\n");

            await ForwardToClient(portData.ProtocolName, bytes);
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] 處理原始資料發生錯誤: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 將封包轉發到 TCP Client（並行轉發優化）
    /// </summary>
    /// <param name="protocolName"></param>
    /// <param name="data"></param>
    private async Task ForwardToClient(string protocolName, byte[] data)
    {
        if (string.IsNullOrEmpty(protocolName)) return;
        if (data == null || data.Length == 0) return;

        var targets = new System.Collections.Generic.List<TCPClientData>();

        // ✅ 優化：先嘗試直接查找（O(1)），假設 Key == ProtocolName
        if (tcpClients.TryGetValue(protocolName, out var directClient))
        {
            targets.Add(directClient);
        }
        else
        {
            // ✅ 降級：如果直接查找失敗，使用線性掃描（支持 Key != ProtocolName 的情況）
            foreach (var tcpClient in tcpClients.Values.ToList())
            {
                if (tcpClient?.portData == null || tcpClient.portData.ProtocolName != protocolName)
                    continue;

                targets.Add(tcpClient);
            }

            // 只在找不到任何匹配時記錄警告（錯誤情況）
            if (targets.Count == 0)
            {
                LogHelper.LogToConsole($"[Router] 警告：找不到任何匹配 {protocolName} 的 TCP Client", isError: true);
            }
        }

        // ✅ 並行轉發到所有目標客戶端
        if (targets.Count > 0)
        {
            await System.Threading.Tasks.Task.WhenAll(
                targets.Select(client => TrySendToClient(client, protocolName, data))
            );
        }
    }

    /// <summary>
    /// 嘗試發送數據到指定客戶端（帶超時保護和並發重連防護）
    /// </summary>
    private async Task TrySendToClient(TCPClientData tcpClient, string protocolName, byte[] data)
    {
        if (tcpClient?.portData == null)
            return;

        // ✅ 防護 1：如果正在重連，直接跳過（避免與 ConnectWithRetryAsync 並發衝突）
        if (!tcpClient.portData.IsConnected)
            return;

        try
        {
            // ✅ 防護 2：原子地檢查並獲取 tcpClient 和 Stream（使用局部變量避免並發替換）
            var client = tcpClient.tcpClient;
            if (client == null || !client.Connected)
                return;

            var stream = client.GetStream();
            if (stream == null || !stream.CanWrite)
            {
                // 連接已失效，更新狀態
                tcpClient.portData.IsConnected = false;
                UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                    SafeExecution.Safe(() => tcpClient.portData.OnUpdate?.Invoke(tcpClient.portData)));
                return;
            }

            // ✅ 防護 3：添加寫入超時（1秒），避免死連接永久阻塞
            using var cts = new System.Threading.CancellationTokenSource(1000);
            await stream.WriteAsync(data, 0, data.Length, cts.Token);

            // 記錄發送日誌（可通過 MonitorManager 控制是否顯示）
            string parsedMessage = Encoding.UTF8.GetString(data);
            RouterLogHelper.LogSend(tcpClient.portData, MonitorTargetType.TCPClient, parsedMessage);
        }
        catch (System.OperationCanceledException)
        {
            // 寫入超時，視為連接已死（不記錄錯誤，靜默處理）
        }
        catch (System.IO.IOException)
        {
            // Stream 已關閉或網路錯誤（正在重連中，靜默處理）
        }
        catch (System.ObjectDisposedException)
        {
            // TcpClient 已被釋放（正在重連中，靜默處理）
        }
        catch (System.InvalidOperationException)
        {
            // Stream 操作無效（正在重連中，靜默處理）
        }
        catch (Exception ex)
        {
            LogHelper.LogToConsole($"[Router] 轉發到 {protocolName} 失敗: {ex.Message}", isError: true);

            try
            {
                tcpClient.tcpClient?.Close();
                tcpClient.tcpClient?.Dispose();
            }
            catch (Exception)
            {
                // 清理資源時的錯誤通常可忽略（連接已斷開）
            }

            tcpClient.tcpClient = null;
            tcpClient.portData.IsConnected = false;

            UnityMainThreadDispatcher.Instance()?.Enqueue(() =>
                SafeExecution.Safe(() => tcpClient.portData.OnUpdate?.Invoke(tcpClient.portData)));
        }
    }

    /// <summary>
    /// 取得 TCP Client 資料
    /// </summary>
    /// <param name="protocolName"></param>
    /// <returns></returns>
    public TCPClientData GetTcpClient(string protocolName)
    {
        tcpClients.TryGetValue(protocolName, out var clientData);
        return clientData;
    }

    /// <summary>
    /// 發送 UDP 封包
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <param name="message"></param>
    private void SendUdp(string ip, int port, string message)
    {
        // ✅ 使用 Task.Run 在後台線程異步發送，避免阻塞
        _ = Task.Run(async () =>
        {
            System.Net.Sockets.UdpClient udp = null;
            try
            {
                udp = new System.Net.Sockets.UdpClient();
                byte[] bytes = Encoding.UTF8.GetBytes(message);

                // ✅ 正確等待異步操作完成
                await udp.SendAsync(bytes, bytes.Length, ip, port);

                LogHelper.LogToMonitor($"[Router] UDP發送成功 {ip}:{port} -> {message}");
            }
            catch (Exception ex)
            {
                LogHelper.LogToMonitor($"[Router] UDP發送失敗 {ip}:{port}: {ex.Message}");
            }
            finally
            {
                // ✅ 確保資源被釋放
                udp?.Close();
                udp?.Dispose();
            }
        });
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
