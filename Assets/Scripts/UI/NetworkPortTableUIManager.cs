using DevKit.Tool;
using UnityEngine;
using DevKit.Console;
using static NetworkPortManager;

public class NetworkPortTableUIManager : MonoBehaviour
{
    #region 欄位宣告

    private PortTablePrefabManager prefabManager;
    private UICollector uiCollector;
    private NetworkSettingsUI networkSettingUI;
    private ConsoleUI consoleUI;

    #endregion

    #region 初始化與反初始化

    public void Init()
    {
        consoleUI = GameObject.FindObjectOfType<ConsoleUI>(true);
        prefabManager = GameObject.FindObjectOfType<PortTablePrefabManager>(true);
        networkSettingUI = GameObject.FindObjectOfType<NetworkSettingsUI>(true);
        uiCollector = GetComponent<UICollector>();

        networkSettingUI.Confirm += OnConfirm;
        NetworkPortManager.Instance.PortDataUpdated += OnUpdate;

        NetworkPortManager.Instance.LoadFromJson();
        NetworkPortManager.Instance.InstantiateTables(prefabManager, uiCollector);
        NetworkPortManager.Instance.AddPortsToNetwork();
    }

    public void DeInit()
    {
        networkSettingUI.Confirm -= OnConfirm;
        NetworkPortManager.Instance.PortDataUpdated -= OnUpdate;
    }

    #endregion

    #region 回呼方法

    private void OnConfirm(PortData portData)
    {
        if (NetworkPortManager.Instance.IsPortUnique(portData))
        {
            var addedPortData = NetworkPortManager.Instance.AddPortData(portData);
            prefabManager.InstantiatePortTable(uiCollector, addedPortData);
        }
        else
        {
            consoleUI.AddLog($"端口或名稱已重複");
        }
    }

    #endregion

    #region 操作端口

    public void OnRemove(PortData portData)
    {
        prefabManager.RefreshAndRecreateTables(uiCollector);
        NetworkPortManager.Instance.RemovePortData(portData);
        NetworkPortManager.Instance.RefreshAndRecreateTables(prefabManager, uiCollector);
    }

    public void OnConnect(PortData portData)
    {
        prefabManager.RefreshAndRecreateTables(uiCollector);
        NetworkPortManager.Instance.ConnectPort(portData);
        NetworkPortManager.Instance.RefreshAndRecreateTables(prefabManager, uiCollector);
    }

    public void OnDisconnectedPort(PortData portData)
    {
        prefabManager.RefreshAndRecreateTables(uiCollector);
        NetworkPortManager.Instance.DisconnectedPort(portData);
        NetworkPortManager.Instance.RefreshAndRecreateTables(prefabManager, uiCollector);
    }

    public void OnMaskType(PortData portData)
    {
        prefabManager.RefreshAndRecreateTables(uiCollector);
        NetworkPortManager.Instance.MaskSwitch(portData);
        NetworkPortManager.Instance.RefreshAndRecreateTables(prefabManager, uiCollector);
    }

    #endregion

    #region 更新和監控

    public void OnUpdate(PortData portData)
    {
        prefabManager.RefreshAndRecreateTables(uiCollector);
        NetworkPortManager.Instance.RefreshAndRecreateTables(prefabManager, uiCollector);
    }

    public void OnMonitorConsole(PortData portData)
    {
        NetworkPortManager.Instance.OnMonitorConsole(portData);
    }

    #endregion
}
