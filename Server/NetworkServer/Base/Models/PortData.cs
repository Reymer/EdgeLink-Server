using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EdgeLink.NetworkServer.Base.Models;

[Serializable]
public class PortDatas
{
    public List<PortData> portDatas = new();
}

[Serializable]
public class PortData
{
    public string Id = "";
    public string Key = "";
    public string ProtocolName = "";
    public string NetProtocol = "";
    public PortDetails LocalPortDetails = new();
    public PortDetails RemotePortDetails = new();
    public string TargetIP = "";
    public bool IsConnected;
    public int COMReceived;
    public int NetReceived;
    public bool IsEnabled = true;
    public string MaskType = "";
    public string ResponseMaskType = "";
    public string RequestMode = "";
    public string SourceProtocolName = "";
    public string SourceProtocolId = "";

    [JsonIgnore] public int CurrentConnections;
    [JsonIgnore] public int TotalConnections;
    [JsonIgnore] public long TotalReceivedBytes;
    [JsonIgnore] public Action<PortData>? OnUpdate;
}

[Serializable]
public class PortDetails
{
    public string Port = "";
    public string Description = "";
}
