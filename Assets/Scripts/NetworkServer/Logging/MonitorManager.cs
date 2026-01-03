/// <summary>
/// 監控目標類型
/// </summary>
public enum MonitorTargetType
{
    TCPServer,
    TCPClient,
    UDP
}

public class MonitorManager
{
    private static MonitorManager _instance;
    public static MonitorManager Instance => _instance ??= new MonitorManager();

    private PortData monitorPortData;
    private MonitorTargetType monitorType;

    private MonitorManager() { }

    /// <summary>
    /// 設置監控端口
    /// </summary>
    /// <param name="portData"></param>
    /// <param name="type"></param>
    public void SetMonitorPort(PortData portData, MonitorTargetType type)
    {
        monitorPortData = portData;
        monitorType = type;
    }

    /// <summary>
    /// 檢查是否正在監控
    /// </summary>
    /// <param name="incomingPort"></param>
    /// <param name="incomingType"></param>
    /// <returns></returns>
    public bool IsMonitoring(PortData incomingPort, MonitorTargetType incomingType)
    {
        return monitorPortData != null &&
               monitorType == incomingType &&
               monitorPortData.ProtocolName == incomingPort.ProtocolName;
    }

    /// <summary>
    /// 獲取監控端口資料
    /// </summary>
    /// <returns></returns>
    public (PortData, MonitorTargetType) GetMonitorInfo()
    {
        return (monitorPortData, monitorType);
    }
}
