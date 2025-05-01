using Newtonsoft.Json;
using System;


/// <summary>
/// 端口資料
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
    public string MaskType { get; set; }

    [JsonIgnore]
    public int CurrentConnections { get; set; } = 0;  

    [JsonIgnore]
    public int TotalConnections { get; set; } = 0;  

    [JsonIgnore]
    public long TotalReceivedBytes { get; set; } = 0;

    [JsonIgnore]
    public Action<PortData> OnUpdate { get; set; }
}


/// <summary>
/// 端口詳細資料
/// </summary>
public class PortDetails
{
    public string Port { get; set; }
    public string Description { get; set; }
}