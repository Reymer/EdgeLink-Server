using System;
using Cysharp.Threading.Tasks;
using DevKit;
using DevKit.Console;
using DevKit.Tool;
using iotserver;
using System.Threading.Tasks;
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

    private readonly ManualAddGuard manualAddGuard = new();
    private PortTableSpawner spawner;
    private NetworkSettingsUI networkSettingsUI;

    public PortTableController(UICollector uiCollector, ConsoleUI consoleUI)
    {
        this.uiCollector = uiCollector;
        this.consoleUI = consoleUI;
    }

    public void Init()
    {
        var monitor = GameObject.FindObjectOfType<Monitor>(true);
        var monitorConsole = GameObject.FindObjectOfType<MonitorConsole>(true);
        spawner = GameObject.FindObjectOfType<PortTableSpawner>(true);
        networkSettingsUI = GameObject.FindObjectOfType<NetworkSettingsUI>(true);
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
            consoleUI.AddLog(Localization.Instance.GetText(LanguageKeys.Log_DuplicatePort));
        }
    }

    public void OnUpdate(PortData portData)
    {
        if (manualAddGuard.IsRunning) return;
        spawner.UpdateTableDynamicUI(portData);
    }

    public void OnPortAdded(PortData portData)
    {
        if (manualAddGuard.IsRunning) return;
        UniTask.Post(() => spawner.InstantiatePortTable(uiCollector, portData), PlayerLoopTiming.Update);
    }

    public void OnPortRemoved(PortData portData)
    {
        UniTask.Post(() =>
        {
            spawner.RefreshAndRecreateTables();
            NetworkPortManager.Instance.RefreshAndRecreateTables(spawner, uiCollector);
        }, PlayerLoopTiming.Update);
    }

    public void OnPortModified(PortData portData)
    {
        UniTask.Post(() =>
        {
            spawner.RefreshAndRecreateTables();
            NetworkPortManager.Instance.RefreshAndRecreateTables(spawner, uiCollector);
        }, PlayerLoopTiming.Update);
    }

    public async void OnRemove(PortData portData)
    {
        try
        {
            await ExecuteWithRefreshAsync(async () => await NetworkPortManager.Instance.RemovePortData(portData));
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[PortTableController] OnRemove failed: {ex.Message}");
        }
    }

    public void OnConnect(PortData portData)
    {
        NetworkPortManager.Instance.ConnectPort(portData);
        spawner.UpdateTableDynamicUI(portData);
    }

    public async void OnDisconnectedPort(PortData portData)
    {
        try
        {
            await NetworkPortManager.Instance.DisconnectedPort(portData);
            spawner.UpdateTableDynamicUI(portData);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[PortTableController] OnDisconnectedPort failed: {ex.Message}");
        }
    }

    public void OnMaskType(PortData portData)
    {
        _ = NetworkPortManager.Instance.MaskSwitch(portData);
    }

    public void OnMonitorConsole(PortData portData)
    {
        NetworkPortManager.Instance.OnMonitorConsole(portData);
    }

    public void OnEdit(PortData portData)
    {
        networkSettingsUI?.OpenForEdit(portData);
    }

    public async void OnEditConfirm(PortData oldPortData, PortData newPortData)
    {
        try
        {
            await NetworkPortManager.Instance.UpdatePortData(oldPortData, newPortData);
        }
        catch (InvalidOperationException)
        {
            consoleUI.AddLog(Localization.Instance.GetText(LanguageKeys.Log_DuplicatePort));
        }
    }

    private async Task ExecuteWithRefreshAsync(System.Func<System.Threading.Tasks.Task> asyncAction)
    {
        spawner.RefreshAndRecreateTables();
        await asyncAction();
        NetworkPortManager.Instance.RefreshAndRecreateTables(spawner, uiCollector);
    }
}
