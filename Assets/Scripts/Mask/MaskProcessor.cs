using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public static class MaskProcessor
{
    private static readonly Regex PlaceholderPattern =
        new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public static string Process(MaskDefinition def, byte[] rawBytes, string textMessage, Dictionary<string, string> extraFields = null)
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

        if (extraFields != null)
            foreach (var kv in extraFields)
                fields[kv.Key] = kv.Value;

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
        foreach (Match m in PlaceholderPattern.Matches(template))
        {
            if (!fields.ContainsKey(m.Groups[1].Value))
                return "";
        }

        // MatchEvaluator 逐一替換原始樣板的佔位符，替換結果不會被再次掃描，
        // 因此欄位值包含 '{' / '}' 也不會干擾其他佔位符的替換。
        return PlaceholderPattern.Replace(template, m =>
            fields.TryGetValue(m.Groups[1].Value, out var val) ? val : m.Value);
    }
}
