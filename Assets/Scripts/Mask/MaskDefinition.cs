using System;
using System.Collections.Generic;

[Serializable]
public class MaskDefinitions
{
    public List<MaskDefinition> definitions = new();
}

[Serializable]
public class MaskDefinition
{
    public string maskId;
    public string localizationKey;
    public string description;
    public string fieldDelimiter;
    public string kvSeparator;
    public string outputTemplate;
    public string sampleData;

    /// <summary>反向路由模式：response（回給發送者）| broadcast（廣播給所有連入的 client）</summary>
    public string routeMode;

    /// <summary>Concurrent 模式用：回應訊息中代表 correlation ID 的欄位名稱</summary>
    public string correlationIdField;
}
