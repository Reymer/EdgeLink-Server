using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevKit.Console;
using DevKit.Tool;
using Newtonsoft.Json;
using UnityEngine;

public class NetworkPortManager
{
    private static readonly Lazy<NetworkPortManager> instance = new(() => new NetworkPortManager());
    public static NetworkPortManager Instance => instance.Value;
    private readonly string filePath;
    private ConsoleUI consoleUI;
    private MonitorConsole monitorConsole;
    public NetworkConnectorCore networkConnectorCore = new();
    private readonly PortDataStorageService storageService; // 儲存端口資料的服務
    private readonly NetPortRegistry portRegistry = new();  // 註冊端口的服務
    public Action<PortData> PortDataUpdated;


    /// <summary>
    /// 單例模式
    /// </summary>
    /// <param name="customFilePath"></param>
    private NetworkPortManager(string customFilePath = null)
    {
        filePath = string.IsNullOrEmpty(customFilePath) ? Path.Combine(Application.dataPath, "portData.json") : customFilePath;
        storageService = new(filePath);
    }

    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        consoleUI = GameObject.FindObjectOfType<ConsoleUI>(true);
        monitorConsole = GameObject.FindObjectOfType<MonitorConsole>(true);
        networkConnectorCore.Init(consoleUI, monitorConsole);
        consoleUI.AddLog($"檔案路徑已設定為: {filePath}");
    }

    /// <summary>
    /// 新增端口資料
    /// </summary>
    /// <param name="portData"></param>
    /// <returns></returns>
    public PortData AddPortData(PortData portData)
    {
        var data = new PortData
        {
            ProtocolName = portData.ProtocolName,
            NetProtocol = portData.NetProtocol,
            LocalPortDetails = new PortDetails { Port = portData.LocalPortDetails.Port },
            RemotePortDetails = new PortDetails { Port = portData.RemotePortDetails.Port },
            IsConnected = true,
            TargetIP = portData.TargetIP,
            COMReceived = 0,
            NetReceived = 0,
            OnUpdate = OnUpdate,
            MaskType = portData.MaskType,
        };

        var type = ParseProtocolType(data.NetProtocol);
        string key = GetPortKey(data);

        portRegistry.Add(type, key, data);
        networkConnectorCore.AddPort(data);
        SaveData();
        return data;
    }

    /// <summary>
    /// 解析協定類型
    /// </summary>
    /// <param name="proto"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    private static NetProtocolType ParseProtocolType(string proto)
    {
        return proto.ToLower() switch
        {
            "tcp server" => NetProtocolType.TcpServer,
            "tcp client" => NetProtocolType.TcpClient,
            "udp" => NetProtocolType.Udp,
            _ => throw new ArgumentException("Unknown protocol: " + proto)
        };
    }

    /// <summary>
    /// 獲取端口的唯一鍵
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private static string GetPortKey(PortData data)
    {
        return ParseProtocolType(data.NetProtocol) switch
        {
            NetProtocolType.TcpServer => data.LocalPortDetails.Port,
            NetProtocolType.TcpClient => data.RemotePortDetails.Port,
            NetProtocolType.Udp => data.RemotePortDetails.Port,
            _ => ""
        };
    }

    /// <summary>
    /// 檢查端口是否唯一
    /// </summary>
    /// <param name="portData"></param>
    /// <returns></returns>
    public bool IsPortUnique(PortData portData)
    {
        var type = ParseProtocolType(portData.NetProtocol);
        string key = GetPortKey(portData);

        bool nameExistsInSameProtocol = portRegistry
            .GetAll()
            .Where(p => ParseProtocolType(p.NetProtocol) == type)
            .Any(p => p.ProtocolName.Equals(portData.ProtocolName, StringComparison.OrdinalIgnoreCase));

        bool portExists = portRegistry.Contains(type, key);

        return !nameExistsInSameProtocol && !portExists;
    }


    /// <summary>
    /// 刪除網路協定資料
    /// </summary>
    /// <param name="portData"></param>
    public void RemovePortData(PortData portData)
    {
        var type = ParseProtocolType(portData.NetProtocol);
        string key = GetPortKey(portData);
        var data = portRegistry.Get(type, key);

        if (data != null)
        {
            data.OnUpdate -= OnUpdate;
            networkConnectorCore.Stop(data);
            portRegistry.Remove(type, key);
            SaveData();
        }
    }

    /// <summary>
    /// 連線方法
    /// </summary>
    /// <param name="portData"></param>
    public void ConnectPort(PortData portData)
    {
        networkConnectorCore.Connected(portData);
    }

    /// <summary>
    /// 斷線方法
    /// </summary>
    /// <param name="portData"></param>
    public void DisconnectedPort(PortData portData)
    {
        networkConnectorCore.Disconnected(portData);
    }

    /// <summary>
    /// 更換遮罩
    /// </summary>
    /// <param name="portData"></param>
    public void MaskSwitch(PortData portData)
    {
        networkConnectorCore.RestartPort(portData);
    }

    /// <summary>
    /// 當監控控制台時觸發事件
    /// </summary>
    /// <param name="portData"></param>
    public void OnMonitorConsole(PortData portData)
    {
        networkConnectorCore.MonitorConsole(portData);
    }

    /// <summary>
    /// 當端口資料更新時觸發事件
    /// </summary>
    /// <param name="data"></param>
    public void OnUpdate(PortData data)
    {
        PortDataUpdated?.Invoke(data);
    }

    /// <summary>
    /// 刷新並重新實例化所有端口表格
    /// </summary>
    /// <param name="prefabManager"></param>
    /// <param name="uiCollector"></param>
    public void RefreshAndRecreateTables(PortTableSpawner prefabManager, UICollector uiCollector)
    {
        InstantiateTables(prefabManager, uiCollector);
    }

    /// <summary>
    /// 實例化所有端口表格
    /// </summary>
    /// <param name="prefabManager"></param>
    /// <param name="uiCollector"></param>
    public void InstantiateTables(PortTableSpawner prefabManager, UICollector uiCollector)
    {
        foreach (var portData in GetPortDatas())
        {
            prefabManager.InstantiatePortTable(uiCollector, portData);
        }
    }

    /// <summary>
    /// 獲取所有端口資料
    /// </summary>
    /// <returns></returns>
    public List<PortData> GetPortDatas() => portRegistry.GetAll().ToList();

    /// <summary>
    /// 載入資料
    /// </summary>
    public void LoadData()
    {
        var loaded = storageService.Load();
        portRegistry.Clear();

        foreach (var data in loaded)
        {
            data.OnUpdate += OnUpdate;
            var type = ParseProtocolType(data.NetProtocol);
            string key = GetPortKey(data);
            portRegistry.Add(type, key, data);
        }
    }

    /// <summary>
    /// 將所有端口添加到網路連接器
    /// </summary>
    public void AddPortsToNetwork()
    {
        foreach (var port in portRegistry.GetAll())
        {
            networkConnectorCore.AddPort(port);
        }
    }

    /// <summary>
    /// 釋放資源
    /// </summary>
    public void UnInit()
    {
        networkConnectorCore.UnInit();
        foreach (var data in portRegistry.GetAll())
            data.OnUpdate -= OnUpdate;
        PortDataUpdated = null;
        SaveData();
    }

    /// <summary>
    /// 儲存資料
    /// </summary>
    public void SaveData() => storageService.Save(portRegistry.GetAll().ToList());
}
