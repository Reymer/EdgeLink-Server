using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public static class MaskProcessor
{
    private static readonly Regex PlaceholderPattern =
        new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public static string Process(MaskDefinition def, byte[] rawBytes, string textMessage)
    {
        if (def == null) return textMessage;

        if (def.outputTemplate == "{raw}" || string.IsNullOrEmpty(def.outputTemplate))
            return textMessage;

        Dictionary<string, string> fields;
        try
        {
            fields = def.inputEncoding == "binary"
                ? ExtractBinaryFields(def, rawBytes)
                : ExtractTextFields(def, textMessage);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MaskProcessor] 欄位解析失敗 ({def.maskId}): {ex}");
            return "";
        }

        return ApplyTemplate(def.outputTemplate, fields);
    }

    private static Dictionary<string, string> ExtractTextFields(MaskDefinition def, string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return result;

        var fieldDelim = string.IsNullOrEmpty(def.fieldDelimiter) ? ";" : def.fieldDelimiter;
        var kvSep = string.IsNullOrEmpty(def.kvSeparator) ? ":" : def.kvSeparator;

        foreach (var field in text.Split(new[] { fieldDelim }, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = field.IndexOf(kvSep, StringComparison.Ordinal);
            if (idx < 0) continue;
            var key = field.Substring(0, idx).Trim();
            var val = field.Substring(idx + kvSep.Length).Trim();
            if (!string.IsNullOrEmpty(key))
                result[key] = val;
        }

        return result;
    }

    private static Dictionary<string, string> ExtractBinaryFields(MaskDefinition def, byte[] data)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data == null || def.binaryFields == null) return result;

        foreach (var rule in def.binaryFields)
        {
            if (string.IsNullOrEmpty(rule.name)) continue;
            if (rule.offset < 0 || rule.length <= 0) continue;
            if (rule.offset + rule.length > data.Length) continue;

            result[rule.name] = ReadBinaryValue(data, rule.offset, rule.length, rule.dataType);
        }

        return result;
    }

    private static string ReadBinaryValue(byte[] data, int offset, int length, string dataType)
    {
        switch (dataType)
        {
            case "uint8":
                return data[offset].ToString();

            case "uint16_le":
                if (length < 2) return "";
                return BitConverter.ToUInt16(data, offset).ToString();

            case "uint16_be":
                if (length < 2) return "";
                return ((ushort)((data[offset] << 8) | data[offset + 1])).ToString();

            case "int32_le":
                if (length < 4) return "";
                return BitConverter.ToInt32(data, offset).ToString();

            case "float_le":
                if (length < 4) return "";
                return BitConverter.ToSingle(data, offset).ToString("G");

            case "hex":
                return BitConverter.ToString(data, offset, length).Replace("-", "");

            default:
                return BitConverter.ToString(data, offset, length).Replace("-", "");
        }
    }

    private static string ApplyTemplate(string template, Dictionary<string, string> fields)
    {
        // 未填佔位符的偵測必須在替換前做，否則欄位值本身含 '{' / '}' 會被誤判整段丟空。
        foreach (Match m in PlaceholderPattern.Matches(template))
        {
            if (!fields.ContainsKey(m.Groups[1].Value))
                return "";
        }

        var sb = new StringBuilder(template);
        foreach (var kv in fields)
            sb.Replace("{" + kv.Key + "}", kv.Value);
        return sb.ToString();
    }
}
