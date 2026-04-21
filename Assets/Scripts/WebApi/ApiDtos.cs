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
    public string protocolName;
    public string netProtocol;
    public string maskType;
    public string localPort;
    public string remotePort;
    public string targetIp;
    public bool isConnected;
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
public class BinaryFieldRuleDto
{
    public string name;
    public int offset;
    public int length;
    public string dataType;
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
}

[Serializable]
public class DeletePortReq
{
    public string protocolName;
    public string netProtocol;
    public string localPort;
    public string remotePort;
}

[Serializable]
public class LanguageReq
{
    public string languageCode;
}

[Serializable]
public class MonitorPortReq
{
    public string protocolName;
    public string netProtocol;
    public string localPort;
    public string remotePort;
}

[Serializable]
public class MonitorPortResponse
{
    public string protocolName;
}

[Serializable]
public class MaskDefinitionDto
{
    public string maskId;
    public string localizationKey;
    public string description;
    public string inputEncoding;
    public string fieldDelimiter;
    public string kvSeparator;
    public List<BinaryFieldRuleDto> binaryFields = new();
    public string outputTemplate;
    public string sampleData;
}
