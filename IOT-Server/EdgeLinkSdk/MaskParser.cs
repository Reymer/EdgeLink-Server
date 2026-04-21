using System;
using System.Collections.Generic;
using System.Text;

namespace EdgeLink
{
    public class MaskParser
    {
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
            {
                var tmpl = tmplParts[i];
                var out_ = outParts[i];

                int open  = tmpl.IndexOf('{');
                int close = tmpl.IndexOf('}', open < 0 ? 0 : open);
                if (open < 0 || close < 0) continue;

                var fieldName = tmpl.Substring(open + 1, close - open - 1);
                var prefix    = tmpl.Substring(0, open);
                var suffix    = tmpl.Substring(close + 1);

                if (!out_.StartsWith(prefix)) continue;
                var value = suffix.Length > 0 && out_.EndsWith(suffix)
                    ? out_.Substring(prefix.Length, out_.Length - prefix.Length - suffix.Length)
                    : out_.Substring(prefix.Length);

                result[fieldName] = value;
            }

            return result;
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
            var sb = new StringBuilder(template);
            foreach (var kv in fields)
                sb.Replace("{" + kv.Key + "}", kv.Value);

            var result = sb.ToString();
            int open   = result.IndexOf('{');
            if (open >= 0 && result.IndexOf('}', open) > open) return string.Empty;
            return result;
        }
    }
}
