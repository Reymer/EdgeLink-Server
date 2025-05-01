using DevKit.Console;
using DevKit.Tool;
using UnityEngine;
public class ManualAddGuard
{
    private bool isRunning = false;
    public bool IsRunning => isRunning;

    public void Start() => isRunning = true;
    public void End() => isRunning = false;
}


public class PortTableController : IPortTableHandler
{
    private readonly UICollector uiCollector;
    private readonly ConsoleUI consoleUI;
    private readonly NetworkPortTableUIManager uiManager;

    private readonly ManualAddGuard manualAddGuard = new();
    private PortTableSpawner spawner;

    public PortTableController(UICollector uiCollector, ConsoleUI consoleUI, NetworkPortTableUIManager uiManager)
    {
        this.uiCollector = uiCollector;
        this.consoleUI = consoleUI;
        this.uiManager = uiManager;
    }

    public void Init()
    {
        var monitor = GameObject.FindObjectOfType<Monitor>(true);
        var monitorConsole = GameObject.FindObjectOfType<MonitorConsole>(true);
        spawner = GameObject.FindObjectOfType<PortTableSpawner>(true);
        spawner.Init(monitor, monitorConsole, this);
    }

    public void LoadAndRenderPortTables()
    {
        NetworkPortManager.Instance.LoadData();
        NetworkPortManager.Instance.AddPortsToNetwork();
        NetworkPortManager.Instance.InstantiateTables(spawner, uiCollector);
    }

    public void OnConfirm(PortData portData)
    {
        if (NetworkPortManager.Instance.IsPortUnique(portData))
        {
            manualAddGuard.Start();
            try
            {
                var added = NetworkPortManager.Instance.AddPortData(portData);
                spawner.InstantiatePortTable(uiCollector, added);
            }
            finally
            {
                manualAddGuard.End();
            }
        }
        else
        {
            consoleUI.AddLog($"端口或名稱已重複");
        }
    }

    public void OnUpdate(PortData portData)
    {
        if (manualAddGuard.IsRunning) return;

        spawner.RefreshAndRecreateTables(uiCollector);
        NetworkPortManager.Instance.InstantiateTables(spawner, uiCollector);
    }

    public void OnRemove(PortData portData)
    {
        ExecuteWithRefresh(() => NetworkPortManager.Instance.RemovePortData(portData));
    }

    public void OnConnect(PortData portData)
    {
        ExecuteWithRefresh(() => NetworkPortManager.Instance.ConnectPort(portData));
    }

    public void OnDisconnectedPort(PortData portData)
    {
        ExecuteWithRefresh(() => NetworkPortManager.Instance.DisconnectedPort(portData));
    }

    public void OnMaskType(PortData portData)
    {
        ExecuteWithRefresh(() => NetworkPortManager.Instance.MaskSwitch(portData));
    }

    public void OnMonitorConsole(PortData portData)
    {
        NetworkPortManager.Instance.OnMonitorConsole(portData);
    }

    private void ExecuteWithRefresh(System.Action action)
    {
        spawner.RefreshAndRecreateTables(uiCollector);
        action();
        NetworkPortManager.Instance.RefreshAndRecreateTables(spawner, uiCollector);
    }
}
