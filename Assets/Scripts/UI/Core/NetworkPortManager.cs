using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevKit;
using DevKit.Console;
using DevKit.Tool;
using iotserver;
using UnityEngine;

public class NetworkPortManager
{
    private static readonly Lazy<NetworkPortManager> instance = new(() => new NetworkPortManager());
    public static NetworkPortManager Instance => instance.Value;
    public NetworkConnectorCore networkConnectorCore = new(); // 網路連接器核心
    private readonly PortDataStorageService storageService; // 儲存端口資料的服務
    private readonly NetPortRegistry portRegistry = new();  // 註冊端口的服務
    public Action<PortData> PortDataUpdated; // 端口資料更新事件
    public Action<PortData> PortDataAdded;   // Web API 新增端口事件
    public Action<PortData> PortDataRemoved; // Web API 刪除端口事件
    public TcpClientRetryConfig retryConfig; // 重試配置

    /// <summary>
    /// 註冊端口資料的服務
    /// </summary>
    private int loadedPortCount = 0;

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

            foreach (var data in ports)
            {
                if (string.IsNullOrEmpty(data.Id))
                    data.Id = Guid.NewGuid().ToString("N");
                data.OnUpdate += OnUpdate;
                var type = ParseProtocolType(data.NetProtocol);
                var key = GetPortKey(data);
                data.Key = key;
                portRegistry.Add(type, key, data);
            }

            loadedPortCount = ports.Count;
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
        if (loadedPortCount > 0)
            LogHelper.LogToConsole(string.Format(Localization.Instance.GetText(LanguageKeys.Log_DataLoaded), loadedPortCount));
    }

    /// <summary>
    /// 取得所有 Port 資料（供 Web API 使用）
    /// </summary>
    public IEnumerable<PortData> GetAllPortDatas() => portRegistry.GetAll();

    /// <summary>
    /// 新增端口資料
    /// </summary>
    /// <param name="portData"></param>
    /// <returns></returns>
    public PortData AddPortData(PortData portData)
    {
        var type = ParseProtocolType(portData.NetProtocol);
        string key = GetPortKey(portData);
        var data = new PortData
        {
            Id = string.IsNullOrEmpty(portData.Id) ? Guid.NewGuid().ToString("N") : portData.Id,
            Key = key,
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
            SourceProtocolName = portData.SourceProtocolName ?? "",
            SourceProtocolId = portData.SourceProtocolId ?? "",
        };

        portRegistry.Add(type, key, data);
        try
        {
            networkConnectorCore.AddPort(data);
        }
        catch
        {
            portRegistry.Remove(type, key);
            throw;
        }
        SaveData();
        PortDataAdded?.Invoke(data);
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
            NetProtocolType.Udp => portData.RemotePortDetails.Port,
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
    public async Task RemovePortData(PortData portData)
    {
        var type = ParseProtocolType(portData.NetProtocol);
        string key = GetPortKey(portData);
        var data = portRegistry.Get(type, key);

        if (data != null)
        {
            data.OnUpdate -= OnUpdate;
            await networkConnectorCore.Stop(data);
            portRegistry.Remove(type, key);
            data.OnUpdate = null;
            SaveData();
            PortDataRemoved?.Invoke(data);
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
    public async System.Threading.Tasks.Task DisconnectedPort(PortData portData)
    {
        await networkConnectorCore.Disconnected(portData);
    }

    /// <summary>
    /// 更換遮罩
    /// </summary>
    /// <param name="portData"></param>
    public async System.Threading.Tasks.Task MaskSwitch(PortData portData)
    {
        await networkConnectorCore.RestartPort(portData);
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
            if (string.IsNullOrEmpty(data.Id))
                data.Id = Guid.NewGuid().ToString("N");
            data.OnUpdate += OnUpdate;
            var type = ParseProtocolType(data.NetProtocol);
            string key = GetPortKey(data);
            data.Key = key;
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
            try
            {
                networkConnectorCore.AddPort(port);
            }
            catch (Exception ex)
            {
                LogHelper.LogToConsole($"[NetworkPortManager] {port.NetProtocol} '{port.ProtocolName}' 啟動失敗: {ex.Message}", isError: true);
            }
        }
    }

    /// <summary>
    /// 釋放資源
    /// </summary>
    public async Task UnInit()
    {
        await networkConnectorCore.UnInit();
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
