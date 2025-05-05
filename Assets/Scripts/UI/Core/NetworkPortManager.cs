using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using DevKit;
using DevKit.Console;
using DevKit.Tool;
using Newtonsoft.Json;
using UnityEngine;

public class NetworkPortManager
{
    private static readonly Lazy<NetworkPortManager> instance = new(() => new NetworkPortManager());
    public static NetworkPortManager Instance => instance.Value;
    private readonly string filePath;
    public NetworkConnectorCore networkConnectorCore = new(); // 網路連接器核心
    private readonly PortDataStorageService storageService; // 儲存端口資料的服務
    private readonly NetPortRegistry portRegistry = new();  // 註冊端口的服務
    public Action<PortData> PortDataUpdated; // 端口資料更新事件
    public TcpClientRetryConfig retryConfig; // 重試配置

    /// <summary>
    /// 註冊端口資料的服務
    /// </summary>
    private NetworkPortManager()
    {
        storageService = new PortDataStorageService();

        try
        {
            retryConfig = storageService.LoadRetryConfig();
            var ports = storageService.LoadPortData();
            if (ports == null || ports.Count == 0)
            {
                Debug.LogWarning("未載入任何 PortData，將不註冊任何端口資料");
                return;
            }
            LogHelper.LogToConsole($"成功載入 {ports.Count} 筆資料");

            foreach (var data in ports)
            {
                data.OnUpdate += OnUpdate;
                var type = ParseProtocolType(data.NetProtocol);
                var key = GetPortKey(data);
                portRegistry.Add(type, key, data);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"初始化 NetworkPortManager 時發生錯誤: {ex.Message}");
        }
    }

    /// <summary>
    /// 獲取 Client 重試配置
    /// </summary>
    /// <returns></returns>
    public TcpClientRetryConfig GetTcpClientRetryConfig()
    {
        return retryConfig;
    }

    /// <summary>
    /// 初始化
    /// </summary>
    public void Init(ConsoleUI consoleUi, MonitorConsole monitorConsole)
    {
        networkConnectorCore.Init(consoleUi, monitorConsole);
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
    private string GetPortKey(PortData portData)
    {
        var type = ParseProtocolType(portData.NetProtocol);
        return type switch
        {
            NetProtocolType.TcpClient => $"{portData.TargetIP}:{portData.RemotePortDetails.Port}",
            NetProtocolType.TcpServer => portData.LocalPortDetails.Port,
            NetProtocolType.Udp => portData.LocalPortDetails.Port,
            _ => portData.LocalPortDetails.Port
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
        string key = GetPortKey(portData); // 通常是 LocalPort 或 Local+Remote 的唯一組合

        // 僅檢查 port 是否存在，不檢查 ProtocolName 重複
        return !portRegistry.Contains(type, key);
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
        var loaded = storageService.LoadPortData();
        if (loaded == null || loaded.Count == 0)
        {
            return;
        }
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
        {
            data.OnUpdate -= OnUpdate;
        }
        PortDataUpdated = null;
        SaveData();
    }

    /// <summary>
    /// 儲存資料
    /// </summary>
    public void SaveData()
    {
        storageService.SavePortData(portRegistry.GetAll().ToList());
    }
}
