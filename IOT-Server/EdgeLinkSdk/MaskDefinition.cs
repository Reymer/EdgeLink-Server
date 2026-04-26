using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace EdgeLink
{
    [DataContract]
    public class MaskDefinition
    {
        [DataMember] public string maskId          { get; set; }
        [DataMember] public string localizationKey { get; set; }
        [DataMember] public string description     { get; set; }
        [DataMember] public string fieldDelimiter  { get; set; }
        [DataMember] public string kvSeparator     { get; set; }
        [DataMember] public List<BinaryFieldRule> binaryFields { get; set; } = new List<BinaryFieldRule>();
        [DataMember] public string outputTemplate  { get; set; }
        [DataMember] public string sampleData      { get; set; }

        static readonly DataContractJsonSerializer _s =
            new DataContractJsonSerializer(typeof(MaskDefinition));

        /// <summary>從 MaskEditor 匯出的 JSON 字串建立 MaskDefinition。</summary>
        public static MaskDefinition FromJson(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var def = (MaskDefinition)_s.ReadObject(ms)
                    ?? throw new ArgumentException("無效的 JSON");
                def.maskId          ??= string.Empty;
                def.localizationKey ??= string.Empty;
                def.description     ??= string.Empty;
                def.fieldDelimiter  ??= ";";
                def.kvSeparator     ??= ":";
                def.outputTemplate  ??= string.Empty;
                def.sampleData      ??= string.Empty;
                return def;
            }
        }

        /// <summary>從 MaskEditor 匯出的 JSON 檔案建立 MaskDefinition。</summary>
        public static MaskDefinition FromJsonFile(string path) =>
            FromJson(File.ReadAllText(path));
    }
}
