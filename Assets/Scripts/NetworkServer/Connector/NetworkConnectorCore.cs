using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DevKit;
using DevKit.Console;
using iotserver;

public class NetworkConnectorCore
{
    private readonly Dictionary<string, NetworkConnectorBase> connectors = new()
    {
        { "UDP", new UdpConnector() },
        { "TCP SERVER", new TCPServerConnector() },
        { "TCP CLIENT", new TCPClientConnector() }
    };

    public void Init(ConsoleUI consoleUI, MonitorConsole monitorConsole)
    {
        LogHelper.Init(monitorConsole, consoleUI);
    }

    public void AddPort(PortData portData)
    {
        string protocol = portData.NetProtocol.ToUpperInvariant();

        if (connectors.TryGetValue(protocol, out var connector))
        {
            connector.AddPort(portData);
        }
        else
        {
            LogHelper.LogToConsole($"{Localization.Instance.GetText(LanguageKeys.Log_UnknownProtocol)}: {portData.NetProtocol}", isError: true);
        }
    }

    public async UniTask RestartPort(PortData portData)
    {
        if (connectors.TryGetValue(portData.NetProtocol.ToUpperInvariant(), out var connector))
            await connector.RestartPort(portData);
    }

    public void Connected(PortData portData)
    {
        if (connectors.TryGetValue(portData.NetProtocol.ToUpperInvariant(), out var connector))
            connector.Connect(portData);
    }

    public async UniTask Disconnected(PortData portData)
    {
        if (connectors.TryGetValue(portData.NetProtocol.ToUpperInvariant(), out var connector))
            await connector.Disconnect(portData);
    }

    public async UniTask Stop(PortData portData)
    {
        string protocol = portData.NetProtocol.ToUpperInvariant();

        if (connectors.TryGetValue(protocol, out var connector))
            await connector.RemovePort(portData);
    }

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

    private async UniTask ShutdownClientsAsync()
    {
        var tasks = new List<UniTask>();
        foreach (var connector in connectors.Values)
            tasks.Add(connector.ShutdownAsync());
        await UniTask.WhenAll(tasks);
    }

    public async UniTask UnInit()
    {
        await ShutdownClientsAsync();
    }
}
