using DevKit.Tool;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static NetworkPortManager;

public class PortTablePrefabManager : MonoBehaviour
{
    #region 欄位

    [SerializeField] private GameObject prefab;
    private NetworkPortTableUIManager portTableUIManager;
    private Monitor monitor;
    private MonitorConsole monitorConsole;

    #endregion

    #region Unity 生命週期方法

    private void Start()
    {
        portTableUIManager = FindObjectOfType<NetworkPortTableUIManager>();
        monitor = FindObjectOfType<Monitor>(true);
        monitorConsole = FindObjectOfType<MonitorConsole>(true);
    }

    #endregion

    #region 端口表格實例化與管理方法

    public void InstantiatePortTable(UICollector uiCollector, PortData portData)
    {
        Transform parentTransform = uiCollector.GetAsset<GameObject>(UIKey.UI_Tables).transform;
        if (parentTransform != null)
        {
            GameObject instance = Instantiate(prefab, parentTransform);
            instance.SetActive(true);
            InitializeTable(instance, portData);
        }
    }

    public void RefreshAndRecreateTables(UICollector uiCollector)
    {
        Transform parentTransform = uiCollector.GetAsset<GameObject>(UIKey.UI_Tables).transform;
        int childCount = parentTransform.childCount;
        for (int i = childCount - 1; i > 0; i--)
        {
            Transform child = parentTransform.GetChild(i);
            Destroy(child.gameObject);
        }
    }

    #endregion

    #region 表格事件訂閱與處理方法

    private void InitializeTable(GameObject instance, PortData portData)
    {
        if (instance.TryGetComponent<Table>(out var table))
        {
            table.Init(portData);
            SubscribeToTableEvents(table, portData);
        }
        else
        {
            Debug.LogWarning("Table component not found on the instance.");
        }
    }

    private void SubscribeToTableEvents(Table table, PortData portData)
    {
        table.OnDelete += (PortData) => DeletePort(portData);
        table.OnConnect += (PortData) => ConnectPort(portData);
        table.OnDisconnectedt += (PortData) => DisconnectedPort(portData);
        table.OnMonitor += (PortData) => Monitor(portData);
        table.OnMask += (PortData) => SetMaskType(portData);
    }

    private void SetMaskType(PortData portData)
    {
        if (portTableUIManager != null)
        {
            portTableUIManager.OnMaskType(portData);
        }
    }

    #endregion

    #region 端口操作方法

    private void DeletePort(PortData portData)
    {
        if (portTableUIManager != null)
        {
            portTableUIManager.OnRemove(portData);
        }
    }

    private void ConnectPort(PortData portData)
    {
        if (portTableUIManager != null)
        {
            portTableUIManager.OnConnect(portData);
        }
    }

    private void DisconnectedPort(PortData portData)
    {
        if (portTableUIManager != null)
        {
            portTableUIManager.OnDisconnectedPort(portData);
        }
    }

    private void Monitor(PortData portData)
    {
        monitor.SetStatus(UIKey.Monitor_Monitor, true);
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            monitorConsole.RemoveAll();
        });
        portTableUIManager.OnMonitorConsole(portData);
    }

    #endregion
}
