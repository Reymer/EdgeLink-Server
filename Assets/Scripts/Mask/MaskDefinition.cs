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
    public string inputEncoding;    // "text" | "binary"

    // text 模式
    public string fieldDelimiter;   // e.g. ";"
    public string kvSeparator;      // e.g. ":"

    // binary 模式
    public List<BinaryFieldRule> binaryFields = new();

    // 輸出
    public string outputTemplate;   // e.g. "ID={ID}&T={TEMP}\n"  或 "{raw}"

    // 編輯器輔助（不影響執行邏輯，供 web 編輯器還原範例輸入）
    public string sampleData;
}

[Serializable]
public class BinaryFieldRule
{
    public string name;     // 欄位名稱，對應模板 {name}
    public int offset;      // byte 起始位置
    public int length;      // byte 長度
    public string dataType; // "uint8"|"uint16_le"|"uint16_be"|"int32_le"|"float_le"|"hex"
}
