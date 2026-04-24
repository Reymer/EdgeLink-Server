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
            fields = ExtractTextFields(def, textMessage);
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
