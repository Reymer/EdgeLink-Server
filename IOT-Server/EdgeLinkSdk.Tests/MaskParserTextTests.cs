using EdgeLink;
using System.Collections.Generic;
using Xunit;

namespace EdgeLinkSdk.Tests
{
    public class MaskParserTextTests
    {
        private static MaskDefinition TextDef(
            string template,
            string fieldDelim = ";",
            string kvSep = ":") => new MaskDefinition
        {
            maskId = "test",
            inputEncoding = "text",
            fieldDelimiter = fieldDelim,
            kvSeparator = kvSep,
            outputTemplate = template,
        };

        // ── Parse（正向解析）──────────────────────────────────────────────────

        [Fact]
        public void Parse_ExtractsFieldsAndFillsTemplate()
        {
            var parser = new MaskParser(TextDef("ID={ID};TEMP={TEMP}"));
            var result = parser.Parse("ID:2;TEMP:5");

            Assert.Equal("ID=2;TEMP=5", result.Output);
            Assert.Equal("2", result.Fields["ID"]);
            Assert.Equal("5", result.Fields["TEMP"]);
        }

        [Fact]
        public void Parse_RawTemplate_ReturnsMessageAsIs()
        {
            var parser = new MaskParser(TextDef("{raw}"));
            var result = parser.Parse("ID:2;TEMP:5");

            Assert.Equal("ID:2;TEMP:5", result.Output);
        }

        [Fact]
        public void Parse_MissingField_ReturnsEmptyOutput()
        {
            var parser = new MaskParser(TextDef("ID={ID};TEMP={TEMP}"));
            var result = parser.Parse("ID:2");  // TEMP 缺失

            Assert.Equal(string.Empty, result.Output);
        }

        [Fact]
        public void Parse_EmptyMessage_ReturnsEmptyOutput()
        {
            var parser = new MaskParser(TextDef("ID={ID}"));
            var result = parser.Parse(string.Empty);

            Assert.Equal(string.Empty, result.Output);
        }

        [Fact]
        public void Parse_CustomDelimiters()
        {
            var parser = new MaskParser(TextDef("ID={ID},TEMP={TEMP}", fieldDelim: ",", kvSep: "="));
            var result = parser.Parse("ID=10,TEMP=25");

            Assert.Equal("ID=10,TEMP=25", result.Output);
        }

        // ── ParseOutput（反向解析）────────────────────────────────────────────

        [Fact]
        public void ParseOutput_ExtractsFieldsFromProcessedString()
        {
            var parser = new MaskParser(TextDef("ID={ID};TEMP={TEMP}"));
            var fields = parser.ParseOutput("ID=2;TEMP=5");

            Assert.Equal("2", fields["ID"]);
            Assert.Equal("5", fields["TEMP"]);
        }

        [Fact]
        public void ParseOutput_WithPrefixAndSuffix()
        {
            var parser = new MaskParser(TextDef("sensor[{ID}];t={TEMP}"));
            var fields = parser.ParseOutput("sensor[42];t=99");

            Assert.Equal("42", fields["ID"]);
            Assert.Equal("99", fields["TEMP"]);
        }

        [Fact]
        public void ParseOutput_EmptyInput_ReturnsEmptyDict()
        {
            var parser = new MaskParser(TextDef("ID={ID}"));
            var fields = parser.ParseOutput(string.Empty);

            Assert.Empty(fields);
        }

        [Fact]
        public void ParseOutput_RoundTrip_MatchesOriginalValues()
        {
            var def = TextDef("ID={ID};TEMP={TEMP}");
            var parser = new MaskParser(def);

            var forward = parser.Parse("ID:7;TEMP:23");
            var reverse = parser.ParseOutput(forward.Output);

            Assert.Equal("7",  reverse["ID"]);
            Assert.Equal("23", reverse["TEMP"]);
        }

        // ── Regression：欄位值含 '{' / '}' 不應觸發「未填佔位符」誤判 ──────────
        [Fact]
        public void Parse_FieldValueContainsBraces_DoesNotReturnEmpty()
        {
            var parser = new MaskParser(TextDef("PAYLOAD={DATA}"));
            var result = parser.Parse("DATA:{\"k\":1}");

            Assert.Equal("PAYLOAD={\"k\":1}", result.Output);
        }

        // ── Regression：同一 delim-part 內多個佔位符應全部擷取 ────────────────
        [Fact]
        public void ParseOutput_MultiplePlaceholdersPerPart_AllExtracted()
        {
            var parser = new MaskParser(TextDef("A={X}B={Y}"));
            var fields = parser.ParseOutput("A=1B=2");

            Assert.Equal("1", fields["X"]);
            Assert.Equal("2", fields["Y"]);
        }

        // ── Regression：suffix 不吻合時不應 silently 吞掉錯誤資料 ─────────────
        [Fact]
        public void ParseOutput_SuffixMismatch_SkipsField()
        {
            var parser = new MaskParser(TextDef("sensor[{ID}]"));
            var fields = parser.ParseOutput("sensor[42");  // 缺右括號

            Assert.False(fields.ContainsKey("ID"));
        }

        // ── Regression：literal 含 regex 特殊字元（如 '.' '+' '*'）需被正確 escape ──
        [Fact]
        public void ParseOutput_LiteralWithRegexMetachars_IsEscaped()
        {
            var parser = new MaskParser(TextDef("v1.0+{BUILD}"));
            var fields = parser.ParseOutput("v1.0+123");

            Assert.Equal("123", fields["BUILD"]);
        }
    }
}
