using DevKit.Tool;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static NetworkPortManager;

public class PortTablePrefabManager : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    private NetworkPortTableUIManager portTableUIManager;

    private void Start()
    {
        portTableUIManager = FindObjectOfType<NetworkPortTableUIManager>();
    }
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
        var ui = GameObject.FindObjectOfType<Monitor>(true);
        ui.SetStatus(UIKey.Monitor_Monitor, true);
        var monitorConsole = GameObject.FindObjectOfType<MonitorConsole>(true);
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            monitorConsole.RemoveAll();
        });
        portTableUIManager.OnMonitorConsole(portData);

    }
}
