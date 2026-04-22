using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EdgeLink
{
    [Serializable]
    public class MaskDefinition
    {
        public string maskId            { get; set; } = string.Empty;
        public string localizationKey   { get; set; } = string.Empty;
        public string description       { get; set; } = string.Empty;
        public string inputEncoding     { get; set; } = "text";   // "text" | "binary"
        public string fieldDelimiter    { get; set; } = ";";
        public string kvSeparator       { get; set; } = ":";
        public List<BinaryFieldRule> binaryFields { get; set; } = new List<BinaryFieldRule>();
        public string outputTemplate    { get; set; } = string.Empty;
        public string sampleData        { get; set; } = string.Empty;

        private static readonly JsonSerializerOptions _opts =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <summary>從 MaskEditor 匯出的 JSON 字串建立 MaskDefinition。</summary>
        public static MaskDefinition FromJson(string json) =>
            JsonSerializer.Deserialize<MaskDefinition>(json, _opts)
            ?? throw new ArgumentException("無效的 JSON");

        /// <summary>從 MaskEditor 匯出的 JSON 檔案建立 MaskDefinition。</summary>
        public static MaskDefinition FromJsonFile(string path) =>
            FromJson(File.ReadAllText(path));
    }

    [Serializable]
    public class BinaryFieldRule
    {
        public string name     { get; set; } = string.Empty;
        public int    offset   { get; set; }
        public int    length   { get; set; }
        /// <summary>uint8 | uint16_le | uint16_be | int32_le | float_le | hex</summary>
        public string dataType { get; set; } = "hex";
    }
}
