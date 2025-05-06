using System.Threading.Tasks;
using DevKit.Console;

public class NetworkConnectorCore
{
    private readonly UdpConnector udpConnector = new();
    private readonly TCPServerConnector tcpServerConnector = new();
    private readonly TCPClientConnector tcpClientConnector = new();

    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="consoleUI"></param>
    /// <param name="monitorConsole"></param>
    public void Init(ConsoleUI consoleUI, MonitorConsole monitorConsole)
    {
        LogHelper.Init(monitorConsole, consoleUI);
    }

    /// <summary>
    /// 添加端口
    /// </summary>
    /// <param name="portData"></param>
    public void AddPort(PortData portData)
    {
        switch (portData.NetProtocol.ToUpperInvariant())
        {
            case "UDP":
                udpConnector.AddPort(portData);
                break;
            case "TCP CLIENT":
                tcpClientConnector.AddPort(portData);
                break;
            case "TCP SERVER":
                tcpServerConnector.AddPort(portData);
                NetworkMessageRouter.Instance.RegisterTcpServer(portData.ProtocolName, tcpServerConnector.GetServerData(portData));
                break;
            default:
                LogHelper.LogToConsole($"無法識別的連接類型: {portData.NetProtocol}", isError: true);
                break;
        }
    }

    /// <summary>
    /// 重啟端口
    /// </summary>
    /// <param name="portData"></param>
    public void RestartPort(PortData portData)
    {
        switch (portData.NetProtocol.ToUpperInvariant())
        {
            case "TCP CLIENT":
                tcpClientConnector.RestartPort(portData);
                break;
            case "TCP SERVER":
                tcpServerConnector.RestartPort(portData);
                break;
        }
    }

    /// <summary>
    /// 連接端口
    /// </summary>
    /// <param name="portData"></param>
    public void Connected(PortData portData)
    {
        switch (portData.NetProtocol.ToUpperInvariant())
        {
            case "TCP CLIENT":
                tcpClientConnector.Connect(portData);
                break;
            case "TCP SERVER":
                tcpServerConnector.Connect(portData);
                break;
        }
    }

    /// <summary>
    /// 斷開連接端口
    /// </summary>
    /// <param name="portData"></param>
    public void Disconnected(PortData portData)
    {
        switch (portData.NetProtocol.ToUpperInvariant())
        {
            case "UDP":
                udpConnector.Disconnect(portData);
                break;
            case "TCP CLIENT":
                tcpClientConnector.Disconnect(portData);
                break;
            case "TCP SERVER":
                tcpServerConnector.Disconnect(portData);
                break;
        }
    }

    /// <summary>
    /// 停止
    /// </summary>
    /// <param name="portData"></param>
    public void Stop(PortData portData)
    {
        switch (portData.NetProtocol.ToUpperInvariant())
        {
            case "UDP":
                udpConnector.Disconnect(portData);
                break;
            case "TCP CLIENT":
                tcpClientConnector.RemovePort(portData);
                break;
            case "TCP SERVER":
                tcpServerConnector.RemovePort(portData);
                NetworkMessageRouter.Instance.UnregisterTcpServer(portData.ProtocolName);
                break;
        }
    }


    /// <summary>
    /// 監控控制台
    /// </summary>
    /// <param name="portData"></param>
    public void MonitorConsole(PortData portData)
    {
        MonitorCounter.Reset();

        if (portData.NetProtocol.ToUpperInvariant() == "TCP SERVER")
            MonitorManager.Instance.SetMonitorPort(portData, MonitorTargetType.TCPServer);
        else if (portData.NetProtocol.ToUpperInvariant() == "TCP CLIENT")
            MonitorManager.Instance.SetMonitorPort(portData, MonitorTargetType.TCPClient);
        else
            LogHelper.LogToConsole($"[Monitor] 不支援的監控協議類型: {portData.NetProtocol}", isError: true);
    }

    /// <summary>
    /// 關閉所有客戶端
    /// </summary>
    /// <returns></returns>
    private async Task ShutdownClientsAsync()
    {
        await tcpClientConnector.ShutdownAsync();
        await udpConnector.ShutdownAsync();
        await tcpServerConnector.ShutdownAsync();
    }

    public async void UnInit()
    {
        await ShutdownClientsAsync();
    }
}
