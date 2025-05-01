using DevKit.Tool;
using System.Collections.Generic;
using UnityEngine;


public class PortTableSpawner : MonoBehaviour
{
    [SerializeField] private PortTableFactory portTableFactory;
    private PortTableBinder binder;
    private readonly List<GameObject> activeTables = new();

    public void Init(Monitor monitor, MonitorConsole monitorConsole, IPortTableHandler handler)
    {
        this.binder = new PortTableBinder(handler, monitor, monitorConsole);
    }


    /// <summary>
    /// 生成端口表格
    /// </summary>
    /// <param name="uiCollector"></param>
    /// <param name="portData"></param>
    public void InstantiatePortTable(UICollector uiCollector, PortData portData)
    {
        Transform parent = uiCollector.GetAsset<GameObject>(UIKey.UI_Tables)?.transform;
        if (parent == null) return;

        GameObject tableGO = portTableFactory.CreateTable(parent);
        if (tableGO == null) return;

        binder.Bind(tableGO, portData);
        activeTables.Add(tableGO);
    }

    /// <summary>
    /// 生成所有端口表格
    /// </summary>
    /// <param name="uiCollector"></param>
    public void RefreshAndRecreateTables(UICollector uiCollector)
    {
        foreach (var table in activeTables)
        {
            if (table != null) Destroy(table);
        }
        activeTables.Clear();
    }
}
