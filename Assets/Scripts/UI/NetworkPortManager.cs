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
    private readonly NetworkConnectorCore networkConnectorCore = new();
    public readonly Dictionary<string, PortData> tcpServers = new();
    public readonly Dictionary<string, PortData> tcpClients = new();
    public readonly Dictionary<string, PortData> udpPorts = new();
    private readonly List<PortData> portDataList = new();

    public event Action<PortData> PortDataUpdated;

    private NetworkPortManager(string customFilePath = null)
    {
        filePath = string.IsNullOrEmpty(customFilePath) ? Path.Combine(Application.dataPath, "portData.json") : customFilePath;
        Debug.Log($"檔案路徑已設定為: {filePath}");
    }

    public void Init()
    {
        consoleUI = GameObject.FindObjectOfType<ConsoleUI>(true);
        monitorConsole = GameObject.FindObjectOfType<MonitorConsole>(true);
        networkConnectorCore.Init(consoleUI, monitorConsole);
    }

    public class PortData
    {
        public string ProtocolName {  get; set; }
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

    public class PortDetails
    {
        public string Port { get; set; }
        public string Description { get; set; }
    }

    public PortData AddPortData(string protocolName, string protocolType, string remotePort, string localPort, string target, string maskType)
    {
        var data = new PortData
        {
            ProtocolName = protocolName,
            NetProtocol = protocolType,
            LocalPortDetails = new PortDetails { Port = localPort },
            RemotePortDetails = new PortDetails { Port = remotePort },
            IsConnected = true,
            TargetIP = target,
            COMReceived = 0,
            NetReceived = 0,
            OnUpdate = OnUpdate,
            MaskType = maskType,
        };

        AddPortToDictionary(data);
        portDataList.Add(data);
        networkConnectorCore.AddPort(data);
        SavePortDataToFile();
        return data;
    }

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

    public bool IsPortUnique(string protocolType, string remotePort, string localPort)
    {
        if (protocolType.Equals("TCP Server", StringComparison.OrdinalIgnoreCase))
        {
            return !tcpServers.ContainsKey(localPort); 
        }
        else if (protocolType.Equals("TCP Client", StringComparison.OrdinalIgnoreCase))
        {
            return !tcpClients.ContainsKey(remotePort);
        }
        else if (protocolType.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            return !udpPorts.ContainsKey(remotePort);
        }

        return false;
    }

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

    public void ConnectPort(PortData portData)
    {
        networkConnectorCore.AddPort(portData);
    }

    public void DisconnectedPort(PortData portData)
    {
        networkConnectorCore.Disconnected(portData);
    }

    public void MaskSwitch(PortData portData)
    {
        networkConnectorCore.AddPort(portData);
    }

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

}
