using DevKit.Console;
using DevKit.Tool;
using UnityEngine;

public class NetworkPortTableUIManager : MonoBehaviour
{
    [SerializeField] private UICollector uiCollector;
    private ConsoleUI consoleUI;
    private NetworkSettingsUI networkSettingUI;
    private PortTableController portTableController;

    public void Init()
    {
        consoleUI = GameObject.FindObjectOfType<ConsoleUI>(true);
        networkSettingUI = GameObject.FindObjectOfType<NetworkSettingsUI>(true);
        portTableController = new PortTableController(uiCollector, consoleUI);
        portTableController.Init();

        networkSettingUI.Confirm += portTableController.OnConfirm;
        networkSettingUI.EditConfirm += portTableController.OnEditConfirm;
        NetworkPortManager.Instance.PortDataUpdated += portTableController.OnUpdate;
        NetworkPortManager.Instance.PortDataAdded   += portTableController.OnPortAdded;
        NetworkPortManager.Instance.PortDataRemoved += portTableController.OnPortRemoved;

        portTableController.LoadAndRenderPortTables();
    }

    public void UnInit()
    {
        if (networkSettingUI != null)
        {
            networkSettingUI.Confirm -= portTableController.OnConfirm;
            networkSettingUI.EditConfirm -= portTableController.OnEditConfirm;
        }

        NetworkPortManager.Instance.PortDataUpdated -= portTableController.OnUpdate;
        NetworkPortManager.Instance.PortDataAdded   -= portTableController.OnPortAdded;
        NetworkPortManager.Instance.PortDataRemoved -= portTableController.OnPortRemoved;
    }
}
