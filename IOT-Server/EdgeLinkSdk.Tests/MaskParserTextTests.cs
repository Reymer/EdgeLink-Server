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
    }
}
