using DevKit.Tool;
using UnityEngine;

public class PortTableBinder
{
    private readonly IPortTableHandler handler;
    private readonly Monitor monitor;
    private readonly MonitorConsole monitorConsole;

    public PortTableBinder(IPortTableHandler handler, Monitor monitor, MonitorConsole monitorConsole)
    {
        this.handler = handler;
        this.monitor = monitor;
        this.monitorConsole = monitorConsole;
    }

    public void Bind(GameObject instance, PortData portData)
    {
        if (instance.TryGetComponent<Table>(out var table))
        {
            table.Init(portData);
            table.OnDelete += handler.OnRemove;
            table.OnConnect += handler.OnConnect;
            table.OnDisconnectedt += handler.OnDisconnectedPort;
            table.OnMask += handler.OnMaskType;
            table.OnMonitor += (pd) =>
            {
                monitor.SetStatus(UIKey.Monitor_Monitor, true);
                UnityMainThreadDispatcher.Instance().Enqueue(() => monitorConsole.RemoveAll());
                handler.OnMonitorConsole(pd);
            };
        }
        else
        {
            Debug.LogWarning($"Table component not found on {instance.name}");
        }
    }
}
