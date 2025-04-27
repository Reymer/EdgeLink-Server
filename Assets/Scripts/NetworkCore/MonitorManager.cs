using static NetworkPortManager;

public class MonitorManager
{
    private static MonitorManager _instance;
    public static MonitorManager Instance => _instance ??= new MonitorManager();

    private PortData monitorPortData;

    private MonitorManager() { }

    public void SetMonitorPort(PortData portData)
    {
        monitorPortData = portData;
    }

    public bool IsMonitoring(PortData incomingPort)
    {
        return monitorPortData != null && monitorPortData.ProtocolName == incomingPort.ProtocolName;
    }

    public PortData GetMonitorPort()
    {
        return monitorPortData;
    }
}