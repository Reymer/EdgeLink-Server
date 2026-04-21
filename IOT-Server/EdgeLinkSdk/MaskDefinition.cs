using System;
using System.Collections.Generic;

namespace EdgeLink
{
    [Serializable]
    public class MaskDefinition
    {
        public string maskId;
        public string localizationKey;
        public string description;
        public string inputEncoding;   // "text" 或 "binary"
        public string fieldDelimiter;  // 文字模式：欄位分隔符，例如 ";"
        public string kvSeparator;     // 文字模式：鍵值分隔符，例如 ":"
        public List<BinaryFieldRule> binaryFields = new List<BinaryFieldRule>();
        public string outputTemplate;
        public string sampleData;
    }

    [Serializable]
    public class BinaryFieldRule
    {
        public string name;
        public int offset;
        public int length;
        public string dataType; // "uint8" | "uint16_le" | "uint16_be" | "int32_le" | "float_le" | "hex"
    }
}
