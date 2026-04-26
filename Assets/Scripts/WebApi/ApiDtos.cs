using System;
using System.Collections.Generic;

[Serializable]
public class PortListResponse
{
    public List<PortDto> ports = new();
}

[Serializable]
public class PortDto
{
    public string id;
    public string protocolName;
    public string netProtocol;
    public string maskType;
    public string responseMaskType;
    public string requestMode;
    public string localPort;
    public string remotePort;
    public string targetIp;
    public bool isConnected;
    public bool isEnabled;
    public string sourceProtocolName;
    public string sourceProtocolId;
    public int currentConnections;
    public int totalConnections;
    public long totalReceivedBytes;
}

[Serializable]
public class MaskListResponse
{
    public List<string> maskTypes = new();
}

[Serializable]
public class ApiResult
{
    public bool success;
    public string error;
}

[Serializable]
public class ChangeMaskReq
{
    public string maskType;
}

[Serializable]
public class RenameReq
{
    public string newId;
}

[Serializable]
public class AddMaskReq
{
    public string maskId;
    public string localizationKey;
}

[Serializable]
public class LogListResponse
{
    public int total;
    public List<string> logs = new();
}

[Serializable]
public class AddPortReq
{
    public string protocolName;
    public string netProtocol;
    public string localPort;
    public string remotePort;
    public string targetIp;
    public string maskType;
    public string responseMaskType;
    public string requestMode;
    public string sourceProtocolName;
    public string sourceProtocolId;
}

[Serializable]
public class UpdatePortReq
{
    public string protocolName;
    public string netProtocol;
    public string localPort;
    public string remotePort;
    public string targetIp;
    public string maskType;
    public string responseMaskType;
    public string requestMode;
    public string sourceProtocolName;
    public string sourceProtocolId;
}

[Serializable]
public class DeletePortReq
{
    public string id;
}

[Serializable]
public class ToggleEnabledReq
{
    public bool enabled;
}

[Serializable]
public class ClientListResponse
{
    public List<string> clients = new();
}

[Serializable]
public class TcpClientInfo
{
    public string endpoint;
    public float connectedSeconds;
    public float lastActivitySec;
    public long messageCount;
    public long totalBytes;
    public float rateBytesPerSec;
    public float rttMs; // -1 = no measurement yet
}

[Serializable]
public class ClientDetailListResponse
{
    public List<TcpClientInfo> clients = new();
}

[Serializable]
public class LanguageReq
{
    public string languageCode;
}

[Serializable]
public class MonitorPortReq
{
    public string id;
}

[Serializable]
public class MonitorPortResponse
{
    public string protocolName;
}

[Serializable]
public class SettingsExportDto
{
    public List<PortExportDto> ports = new();
    public List<MaskDefinitionDto> masks = new();
}

[Serializable]
public class PortExportDto
{
    public string protocolName;
    public string netProtocol;
    public string localPort;
    public string remotePort;
    public string targetIp;
    public string maskType;
    public string responseMaskType;
    public string requestMode;
    public string sourceProtocolName;
    public string sourceProtocolId;
    public bool isEnabled;
}

[Serializable]
public class MaskDefinitionDto
{
    public string maskId;
    public string localizationKey;
    public string description;
    public string fieldDelimiter;
    public string kvSeparator;
    public string outputTemplate;
    public string sampleData;
    public string routeMode;
    public string correlationIdField;
}
