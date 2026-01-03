using DevKit.Tool;
using System.Collections.Generic;
using UnityEngine;


public class PortTableSpawner : MonoBehaviour
{
    [SerializeField] private PortTableFactory portTableFactory;
    private PortTableBinder binder;
    private readonly List<GameObject> activeTables = new();
    private readonly Dictionary<string, Table> tableMap = new(); // PortData.Key -> Table

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

        // 追蹤 Table 實例
        var table = tableGO.GetComponent<Table>();
        if (table != null && !string.IsNullOrEmpty(portData.Key))
        {
            tableMap[portData.Key] = table;
        }
    }

    /// <summary>
    /// 只更新指定 PortData 的動態 UI（不重新創建）
    /// </summary>
    /// <param name="portData"></param>
    public void UpdateTableDynamicUI(PortData portData)
    {
        if (string.IsNullOrEmpty(portData.Key)) return;

        if (tableMap.TryGetValue(portData.Key, out var table) && table != null)
        {
            table.UpdateDynamicUI(portData);
        }
    }

    /// <summary>
    /// 刪除所有端口表格
    /// </summary>
    /// <param name="uiCollector"></param>
    public void RefreshAndRecreateTables(UICollector uiCollector)
    {
        foreach (var table in activeTables)
        {
            if (table != null) Destroy(table);
        }
        activeTables.Clear();
        tableMap.Clear();
    }
}
