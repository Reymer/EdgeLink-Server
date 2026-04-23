using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace EdgeLink
{
    public class MaskParser
    {
        private static readonly Regex PlaceholderPattern =
            new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

        private readonly MaskDefinition definition;

        public MaskParser(MaskDefinition definition)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>解析文字訊息，回傳欄位與輸出字串。</summary>
        public ParseResult Parse(string message)
        {
            if (definition.outputTemplate == "{raw}" || string.IsNullOrEmpty(definition.outputTemplate))
                return new ParseResult(new Dictionary<string, string>(), message ?? string.Empty);

            try
            {
                var fields = SplitTextFields(message ?? string.Empty);
                var output = FillTemplate(definition.outputTemplate, fields);
                return new ParseResult(fields, output);
            }
            catch (Exception ex) { return new ParseResult(ex.Message); }
        }

        /// <summary>解析二進位位元組，回傳欄位與輸出字串。</summary>
        public ParseResult ParseBinary(byte[] bytes)
        {
            if (definition.outputTemplate == "{raw}" || string.IsNullOrEmpty(definition.outputTemplate))
            {
                var hex = bytes != null ? BitConverter.ToString(bytes).Replace("-", "") : string.Empty;
                return new ParseResult(new Dictionary<string, string>(), hex);
            }

            try
            {
                var fields = SplitBinaryFields(bytes);
                var output = FillTemplate(definition.outputTemplate, fields);
                return new ParseResult(fields, output);
            }
            catch (Exception ex) { return new ParseResult(ex.Message); }
        }

        /// <summary>
        /// 從 IoT Server 已輸出的字串中反向提取欄位值。
        /// 例如 outputTemplate="ID={ID};TEMP={TEMP}"，輸入 "ID=2;TEMP=5"，
        /// 可取得 Fields["ID"]="2"、Fields["TEMP"]="5"。
        /// </summary>
        public Dictionary<string, string> ParseOutput(string processedOutput)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(processedOutput)) return result;

            var template   = definition.outputTemplate;
            if (string.IsNullOrEmpty(template) || template == "{raw}") return result;

            var fieldDelim = string.IsNullOrEmpty(definition.fieldDelimiter) ? ";" : definition.fieldDelimiter;
            var tmplParts  = template.Split(new[] { fieldDelim }, StringSplitOptions.None);
            var outParts   = processedOutput.Split(new[] { fieldDelim }, StringSplitOptions.None);

            for (int i = 0; i < Math.Min(tmplParts.Length, outParts.Length); i++)
                ExtractPartFields(tmplParts[i], outParts[i], result);

            return result;
        }

        // 將 template part 轉成完全錨定的 regex，支援一段內多個 {name} 佔位符；
        // 每個佔位符以非貪婪 (.*?) 取值，literal 文字做 Regex.Escape 避免特殊字元干擾。
        private static void ExtractPartFields(string tmpl, string value, Dictionary<string, string> into)
        {
            var matches = PlaceholderPattern.Matches(tmpl);
            if (matches.Count == 0) return;

            var pattern = new StringBuilder("^");
            var names   = new List<string>(matches.Count);
            int cursor  = 0;
            foreach (Match m in matches)
            {
                pattern.Append(Regex.Escape(tmpl.Substring(cursor, m.Index - cursor)));
                pattern.Append("(.*?)");
                names.Add(m.Groups[1].Value);
                cursor = m.Index + m.Length;
            }
            pattern.Append(Regex.Escape(tmpl.Substring(cursor)));
            pattern.Append('$');

            var match = Regex.Match(value, pattern.ToString());
            if (!match.Success) return;

            for (int i = 0; i < names.Count; i++)
                into[names[i]] = match.Groups[i + 1].Value;
        }

        private Dictionary<string, string> SplitTextFields(string text)
        {
            var result     = new Dictionary<string, string>(StringComparer.Ordinal);
            var fieldDelim = string.IsNullOrEmpty(definition.fieldDelimiter) ? ";" : definition.fieldDelimiter;
            var kvSep      = string.IsNullOrEmpty(definition.kvSeparator)    ? ":" : definition.kvSeparator;

            foreach (var segment in text.Split(new[] { fieldDelim }, StringSplitOptions.RemoveEmptyEntries))
            {
                int sep = segment.IndexOf(kvSep, StringComparison.Ordinal);
                if (sep < 0) continue;
                var key = segment.Substring(0, sep).Trim();
                var val = segment.Substring(sep + kvSep.Length).Trim();
                if (!string.IsNullOrEmpty(key)) result[key] = val;
            }

            return result;
        }

        private Dictionary<string, string> SplitBinaryFields(byte[] data)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (data == null || definition.binaryFields == null) return result;

            foreach (var rule in definition.binaryFields)
            {
                if (string.IsNullOrEmpty(rule.name))         continue;
                if (rule.offset < 0 || rule.length <= 0)     continue;
                if (rule.offset + rule.length > data.Length) continue;
                result[rule.name] = ReadBytes(data, rule.offset, rule.length, rule.dataType);
            }

            return result;
        }

        private static string ReadBytes(byte[] data, int offset, int length, string dataType)
        {
            switch (dataType)
            {
                case "uint8":    return data[offset].ToString();
                case "uint16_le": if (length < 2) return string.Empty; return BitConverter.ToUInt16(data, offset).ToString();
                case "uint16_be": if (length < 2) return string.Empty; return ((ushort)((data[offset] << 8) | data[offset + 1])).ToString();
                case "int32_le":  if (length < 4) return string.Empty; return BitConverter.ToInt32(data, offset).ToString();
                case "float_le":  if (length < 4) return string.Empty; return BitConverter.ToSingle(data, offset).ToString("G");
                case "hex":
                default:          return BitConverter.ToString(data, offset, length).Replace("-", "");
            }
        }

        private static string FillTemplate(string template, Dictionary<string, string> fields)
        {
            // 先驗：若 template 中任一佔位符名稱不在 fields 裡，直接回空字串。
            // 此判斷必須在替換「之前」做，否則欄位值本身含 '{' / '}' 會被誤判為未填佔位符。
            foreach (Match m in PlaceholderPattern.Matches(template))
            {
                if (!fields.ContainsKey(m.Groups[1].Value))
                    return string.Empty;
            }

            var sb = new StringBuilder(template);
            foreach (var kv in fields)
                sb.Replace("{" + kv.Key + "}", kv.Value);
            return sb.ToString();
        }
    }
}
