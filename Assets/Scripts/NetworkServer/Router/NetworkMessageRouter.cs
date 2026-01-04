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

    // ✅ 新增：MaskType 處理器字典（可客製化）
    public delegate void MessageHandler(PortData portData, byte[] rawBytes, string parsedMessage);
    private readonly Dictionary<string, MessageHandler> maskTypeHandlers = new();

    private NetworkMessageRouter()
    {
        // ✅ 註冊預設的 MaskType 處理器
        RegisterMaskType("original data", (portData, rawBytes, parsedMessage) =>
            _ = HandleOriginalData(portData, parsedMessage));
    }

    /// <summary>
    /// 註冊自定義 MaskType 處理器
    /// </summary>
    /// <param name="maskType">MaskType 名稱（例如："Custom Protocol"）</param>
    /// <param name="handler">處理函數</param>
    public void RegisterMaskType(string maskType, MessageHandler handler)
    {
        if (string.IsNullOrWhiteSpace(maskType))
        {
            LogHelper.LogToConsole("[Router] RegisterMaskType 失敗：MaskType 不能為空", isError: true);
            return;
        }

        maskTypeHandlers[maskType] = handler;
        LogHelper.LogToConsole($"[Router] 已註冊 MaskType: {maskType}");
    }

    /// <summary>
    /// 取消註冊 MaskType 處理器
    /// </summary>
    public void UnregisterMaskType(string maskType)
    {
        if (maskTypeHandlers.Remove(maskType))
        {
            LogHelper.LogToConsole($"[Router] 已取消註冊 MaskType: {maskType}");
        }
    }

    /// <summary>
    /// 獲取所有已註冊的 MaskType
    /// </summary>
    public IEnumerable<string> GetRegisteredMaskTypes()
    {
        return maskTypeHandlers.Keys;
    }

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

        // ✅ 使用字典查找處理器（支持客製化 MaskType）
        if (maskTypeHandlers.TryGetValue(maskType, out var handler))
        {
            handler(portData, rawBytes, parsedMessage);
        }
        else
        {
            LogHelper.LogToConsole($"[Router] 未註冊的 MaskType: {maskType}（已註冊: {string.Join(", ", maskTypeHandlers.Keys)}）", isError: true);
        }
    }

    /// <summary>
    /// 處理原始資料
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="message"></param>
    public async Task HandleOriginalData(PortData portData, string message)
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
                return;
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
                MainThreadDispatcher.Instance()?.Enqueue(() =>
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

            MainThreadDispatcher.Instance()?.Enqueue(() =>
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

}
