using System;
using System.Collections.Generic;

/// <summary>
/// 容器：包一個 PortData 的列表，方便 SettingLoader 處理
/// </summary>
[Serializable]
public class PortDatas
{
    /// <summary>所有端口資料，如無也不為 null</summary>
    public List<PortData> portDatas = new();
}

/// <summary>
/// 單筆端口資料
/// </summary>
[Serializable]
public class PortData
{
    public string Key;
    public string ProtocolName;
    public string NetProtocol;
    public PortDetails LocalPortDetails;
    public PortDetails RemotePortDetails;
    public string TargetIP;
    public bool IsConnected;
    public int COMReceived;
    public int NetReceived;
    public string MaskType;

    /// <summary>
    /// 以下都是運行時屬性，不要序列化
    /// </summary>
    [NonSerialized] public int CurrentConnections;
    [NonSerialized] public int TotalConnections;
    [NonSerialized] public long TotalReceivedBytes;
    [NonSerialized] public Action<PortData> OnUpdate;
}

/// <summary>
/// 端口詳細資訊
/// </summary>
[Serializable]
public class PortDetails
{
    public string Port;
    public string Description;
}
