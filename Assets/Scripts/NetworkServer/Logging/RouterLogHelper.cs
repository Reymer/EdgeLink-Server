using static NetworkPortManager;

/// <summary>
/// 路由器日誌輔助類
/// </summary>
public static class RouterLogHelper
{
    /// <summary>
    /// 記錄接收的封包
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="targetType"></param>
    /// <param name="parsedMessage"></param>
    public static void LogReceive(PortData portData, MonitorTargetType targetType, string parsedMessage)
    {
        if (MonitorManager.Instance.IsMonitoring(portData, targetType))
        {
            var count = MonitorCounter.Next();
            LogHelper.LogToMonitor($"[#{count}] [Router] {GetTargetLabel(targetType)} [{portData.ProtocolName}] 收到封包: {parsedMessage}");
        }
    }

    /// <summary>
    /// 記錄發送的封包
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="targetType"></param>
    /// <param name="parsedMessage"></param>
    public static void LogSend(PortData portData, MonitorTargetType targetType, string parsedMessage)
    {
        if (MonitorManager.Instance.IsMonitoring(portData, targetType))
        {
            var count = MonitorCounter.Next();
            LogHelper.LogToMonitor($"[#{count}] [Router] {GetTargetLabel(targetType)} [{portData.ProtocolName}] 傳送封包: {parsedMessage}");
        }
    }

    /// <summary>
    /// 獲取目標標籤
    /// </summary>
    /// <param name="targetType"></param>
    /// <returns></returns>
    private static string GetTargetLabel(MonitorTargetType targetType)
    {
        return targetType switch
        {
            MonitorTargetType.TCPServer => "TCP Server",
            MonitorTargetType.TCPClient => "TCP Client",
            _ => "Unknown"
        };
    }
}
