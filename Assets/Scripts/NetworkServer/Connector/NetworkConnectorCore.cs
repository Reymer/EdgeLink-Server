using System.Collections.Generic;
using System.Threading.Tasks;
using DevKit.Console;

public class NetworkConnectorCore
{
    private readonly Dictionary<string, NetworkConnectorBase> connectors = new()
    {
        { "UDP", new UdpConnector() },
        { "TCP SERVER", new TCPServerConnector() },
        { "TCP CLIENT", new TCPClientConnector() }
    };

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
        string protocol = portData.NetProtocol.ToUpperInvariant();

        if (connectors.TryGetValue(protocol, out var connector))
        {
            connector.AddPort(portData);

            // TCP Server 需要額外註冊到路由器
            if (protocol == "TCP SERVER" && connector is TCPServerConnector tcpServer)
            {
                NetworkMessageRouter.Instance.RegisterTcpServer(portData.ProtocolName, tcpServer.GetServerData(portData));
            }
        }
        else
        {
            LogHelper.LogToConsole($"無法識別的連接類型: {portData.NetProtocol}", isError: true);
        }
    }

    /// <summary>
    /// 重啟端口
    /// </summary>
    /// <param name="portData"></param>
    public async Task RestartPort(PortData portData)
    {
        if (connectors.TryGetValue(portData.NetProtocol.ToUpperInvariant(), out var connector))
        {
            await connector.RestartPort(portData);
        }
    }

    /// <summary>
    /// 連接端口
    /// </summary>
    /// <param name="portData"></param>
    public void Connected(PortData portData)
    {
        if (connectors.TryGetValue(portData.NetProtocol.ToUpperInvariant(), out var connector))
        {
            connector.Connect(portData);
        }
    }

    /// <summary>
    /// 斷開連接端口
    /// </summary>
    /// <param name="portData"></param>
    public async Task Disconnected(PortData portData)
    {
        if (connectors.TryGetValue(portData.NetProtocol.ToUpperInvariant(), out var connector))
        {
            await connector.Disconnect(portData);
        }
    }

    /// <summary>
    /// 停止
    /// </summary>
    /// <param name="portData"></param>
    public async Task Stop(PortData portData)
    {
        string protocol = portData.NetProtocol.ToUpperInvariant();

        if (connectors.TryGetValue(protocol, out var connector))
        {
            await connector.RemovePort(portData);

            // TCP Server 需要額外從路由器註銷
            if (protocol == "TCP SERVER")
            {
                NetworkMessageRouter.Instance.UnregisterTcpServer(portData.ProtocolName);
            }
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
            MonitorManager.Instance.SetMonitorPort(portData, MonitorTargetType.UDP);
    }

    /// <summary>
    /// 關閉所有客戶端
    /// </summary>
    /// <returns></returns>
    private async Task ShutdownClientsAsync()
    {
        var tasks = new List<Task>();
        foreach (var connector in connectors.Values)
        {
            tasks.Add(connector.ShutdownAsync());
        }
        await Task.WhenAll(tasks);
    }

    public async Task UnInit()
    {
        await ShutdownClientsAsync();
    }
}
