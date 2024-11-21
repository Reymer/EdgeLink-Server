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
    #region 單例模式

    private static readonly Lazy<NetworkPortManager> instance = new(() => new NetworkPortManager());
    public static NetworkPortManager Instance => instance.Value;

    #endregion

    #region 欄位

    private readonly string filePath;
    private ConsoleUI consoleUI;
    private MonitorConsole monitorConsole;
    private readonly NetworkConnectorCore networkConnectorCore = new();
    public readonly Dictionary<string, PortData> tcpServers = new();
    public readonly Dictionary<string, PortData> tcpClients = new();
    public readonly Dictionary<string, PortData> udpPorts = new();
    private readonly List<PortData> portDataList = new();
    public event Action<PortData> PortDataUpdated;

    #endregion

    #region 建構函式

    private NetworkPortManager(string customFilePath = null)
    {
        filePath = string.IsNullOrEmpty(customFilePath) ? Path.Combine(Application.dataPath, "portData.json") : customFilePath;
        Debug.Log($"檔案路徑已設定為: {filePath}");
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        consoleUI = GameObject.FindObjectOfType<ConsoleUI>(true);
        monitorConsole = GameObject.FindObjectOfType<MonitorConsole>(true);
        networkConnectorCore.Init(consoleUI, monitorConsole);
    }

    #endregion

    #region PortData 類別

    /// <summary>
    /// 協定資料結構
    /// </summary>
    public class PortData
    {
        public string ProtocolName { get; set; }
        public string NetProtocol { get; set; }
        public PortDetails LocalPortDetails { get; set; }
        public PortDetails RemotePortDetails { get; set; }
        public string TargetIP { get; set; }
        public bool IsConnected { get; set; }
        public int COMReceived { get; set; } = 0;
        public int NetReceived { get; set; } = 0;
        [JsonIgnore]
        public Action<PortData> OnUpdate { get; set; }
        public string MaskType { get; set; }
    }

    /// <summary>
    /// Port號 / 詳細內容
    /// </summary>
    public class PortDetails
    {
        public string Port { get; set; }
        public string Description { get; set; }
    }

    #endregion

    #region 端口管理
    
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

        AddPortToDictionary(data);
        portDataList.Add(data);
        networkConnectorCore.AddPort(data);
        SavePortDataToFile();
        return data;
    }

    /// <summary>
    /// 新增網路協定
    /// </summary>
    /// <param name="data"></param>
    private void AddPortToDictionary(PortData data)
    {
        if (data.NetProtocol.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
        {
            tcpServers[data.LocalPortDetails.Port] = data;
        }
        else if (data.NetProtocol.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            tcpClients[data.RemotePortDetails.Port] = data;
        }
        else if (data.NetProtocol.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            udpPorts[data.RemotePortDetails.Port] = data;
        }
    }

    /// <summary>
    /// 判斷是否可以新增
    /// </summary>
    /// <param name="porData"></param>
    /// <returns></returns>
    public bool IsPortUnique(PortData porData)
    {        
        if (porData.NetProtocol.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
        {
            return !tcpServers.Values.Any(pd => pd.ProtocolName.Equals(porData.ProtocolName, StringComparison.OrdinalIgnoreCase))
                && !tcpServers.ContainsKey(porData.LocalPortDetails.Port);
        }
        else if (porData.NetProtocol.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            return !tcpClients.Values.Any(pd => pd.ProtocolName.Equals(porData.ProtocolName, StringComparison.OrdinalIgnoreCase))
                && !tcpClients.ContainsKey(porData.RemotePortDetails.Port);
        }
        else if (porData.NetProtocol.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            return !udpPorts.Values.Any(pd => pd.ProtocolName.Equals(porData.ProtocolName, StringComparison.OrdinalIgnoreCase))
                && !udpPorts.ContainsKey(porData.RemotePortDetails.Port);
        }

        return false;
    }

    /// <summary>
    /// 刪除網路協定資料
    /// </summary>
    /// <param name="portData"></param>
    public void RemovePortData(PortData portData)
    {
        PortData dataToRemove = GetPortData(portData);

        if (dataToRemove == null)
        {
            Debug.LogWarning($"未能找到協定為 {portData.NetProtocol} 的端口資料，無法移除。");
            return;
        }

        string portToRemove = (portData.NetProtocol.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
            ? dataToRemove.LocalPortDetails.Port
            : dataToRemove.RemotePortDetails.Port;

        Debug.Log($"Removing {portData.NetProtocol} Port: {portToRemove}");
        dataToRemove.OnUpdate -= OnUpdate;
        RemovePortFromDictionary(portData.NetProtocol, portToRemove);

        bool removedFromList = portDataList.Remove(dataToRemove);
        if (!removedFromList)
        {
            Debug.LogWarning($"未能成功從清單中移除協定為 {portData.NetProtocol}，端口為 {portToRemove} 的資料。");
        }
        networkConnectorCore.StopClient(portData);

        SavePortDataToFile();
    }

    #endregion

    #region 連線管理

    /// <summary>
    /// 連線方法
    /// </summary>
    /// <param name="portData"></param>
    public void ConnectPort(PortData portData)
    {
        networkConnectorCore.AddPort(portData);
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
        networkConnectorCore.AddPort(portData);
    }

    #endregion

    #region 輔助方法

    private PortData GetPortData(PortData portData)
    {
        if (portData == null)
        {
            throw new ArgumentNullException(nameof(portData), "PortData cannot be null.");
        }

        PortData resultPortData = null;

        if (portData.NetProtocol.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
        {
            resultPortData = tcpServers.GetValueOrDefault(portData.LocalPortDetails.Port);
        }
        else if (portData.NetProtocol.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            resultPortData = udpPorts.GetValueOrDefault(portData.RemotePortDetails.Port);
        }
        else if (portData.NetProtocol.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            resultPortData = tcpClients.GetValueOrDefault(portData.RemotePortDetails.Port);
        }

        return resultPortData;
    }

    private void RemovePortFromDictionary(string netProtocol, string port)
    {
        bool removed = netProtocol switch
        {
            "TCP Server" => tcpServers.Remove(port),
            "TCP Client" => tcpClients.Remove(port),
            "UDP" => udpPorts.Remove(port),
            _ => false
        };

        if (removed)
        {
            Debug.Log($"Successfully removed {netProtocol} on Port: {port}");
        }
        else
        {
            Debug.LogWarning($"{netProtocol} Port: {port} not found for removal.");
        }
    }

    #endregion

    #region UI 及事件管理

    public void OnMonitorConsole(PortData portData)
    {
        networkConnectorCore.MonitorConsole(portData);
    }

    public void OnUpdate(PortData data)
    {
        PortDataUpdated?.Invoke(data);
    }

    public void RefreshAndRecreateTables(PortTablePrefabManager prefabManager, UICollector uiCollector)
    {
        InstantiateTables(prefabManager, uiCollector);
    }

    public void InstantiateTables(PortTablePrefabManager prefabManager, UICollector uiCollector)
    {
        foreach (var portData in portDataList)
        {
            prefabManager.InstantiatePortTable(uiCollector, portData);
        }
    }

    #endregion

    #region 儲存與載入

    public void AddPortsToNetwork()
    {
        var allPorts = tcpServers.Values.Concat(udpPorts.Values).Concat(tcpClients.Values).ToList();

        foreach (var portData in allPorts)
        {
            networkConnectorCore.AddPort(portData);
        }
    }

    public void LoadFromJson()
    {
        try
        {
            var loadedData = JsonFileHandler.LoadFromJson<PortData>(filePath);
            if (loadedData == null || !loadedData.Any())
            {
                consoleUI.AddLog("載入的資料為空。");
                return;
            }

            portDataList.Clear();
            tcpServers.Clear();
            udpPorts.Clear();
            tcpClients.Clear();

            foreach (var data in loadedData)
            {
                data.OnUpdate += OnUpdate;
                portDataList.Add(data);
                AddPortToDictionary(data);
            }
        }
        catch (Exception ex)
        {
            consoleUI.AddLog("無法載入資料，請檢查文件的格式和路徑。" + ex);
        }
    }

    public void DeInit()
    {
        networkConnectorCore.DeInit();
        foreach (var portData in tcpServers.Values.Concat(udpPorts.Values).Concat(tcpClients.Values))
        {
            portData.OnUpdate -= OnUpdate;
        }

        PortDataUpdated = null;
        SavePortDataToFile();
    }

    private void SavePortDataToFile()
    {
        try
        {
            JsonFileHandler.SaveToJson(filePath, portDataList);
        }
        catch (Exception ex)
        {
            Debug.Log("無法保存端口資料。" + ex);
        }
    }

    #endregion
}
