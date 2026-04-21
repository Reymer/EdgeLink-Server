using DevKit;
using iotserver;
using static NetworkPortManager;

/// <summary>
/// 路由器日誌輔助類
/// </summary>
public static class RouterLogHelper
{
    public static void LogReceive(PortData portData, MonitorTargetType targetType, string parsedMessage)
    {
        if (!MonitorManager.Instance.IsMonitoring(portData, targetType)) return;

        var count = MonitorCounter.Next();
        LogHelper.LogToMonitor($"[#{count}] [Router] {GetTargetLabel(targetType)} [{portData.ProtocolName}] {Localization.Instance.GetText(LanguageKeys.Log_PacketReceived)}: {parsedMessage}");
    }

    public static void LogSend(PortData portData, MonitorTargetType targetType, string parsedMessage)
    {
        if (!MonitorManager.Instance.IsMonitoring(portData, targetType)) return;

        var count = MonitorCounter.Next();
        LogHelper.LogToMonitor($"[#{count}] [Router] {GetTargetLabel(targetType)} [{portData.ProtocolName}] {Localization.Instance.GetText(LanguageKeys.Log_PacketSent)}: {parsedMessage}");
    }

    private static string GetTargetLabel(MonitorTargetType targetType) => targetType switch
    {
        MonitorTargetType.TCPServer => "TCP Server",
        MonitorTargetType.TCPClient => "TCP Client",
        MonitorTargetType.UDP      => "UDP",
        _                          => "Unknown"
    };
}
