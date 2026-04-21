using EdgeLink;
using System;
using Xunit;

namespace EdgeLinkSdk.Tests
{
    public class MaskParserBinaryTests
    {
        private static MaskDefinition BinDef(string template, params BinaryFieldRule[] rules)
        {
            var def = new MaskDefinition
            {
                maskId = "bin_test",
                inputEncoding = "binary",
                outputTemplate = template,
            };
            def.binaryFields.AddRange(rules);
            return def;
        }

        private static BinaryFieldRule Rule(string name, int offset, int length, string dataType) =>
            new BinaryFieldRule { name = name, offset = offset, length = length, dataType = dataType };

        // ── uint8 ─────────────────────────────────────────────────────────────

        [Fact]
        public void ParseBinary_Uint8_ReadsCorrectly()
        {
            var def = BinDef("VAL={VAL}", Rule("VAL", 0, 1, "uint8"));
            var result = new MaskParser(def).ParseBinary(new byte[] { 0xFF });

            Assert.Equal("VAL=255", result.Output);
        }

        // ── uint16_le ─────────────────────────────────────────────────────────

        [Fact]
        public void ParseBinary_Uint16LE_ReadsCorrectly()
        {
            var def = BinDef("T={T}", Rule("T", 0, 2, "uint16_le"));
            var bytes = BitConverter.GetBytes((ushort)1000);  // little-endian
            var result = new MaskParser(def).ParseBinary(bytes);

            Assert.Equal("T=1000", result.Output);
        }

        // ── uint16_be ─────────────────────────────────────────────────────────

        [Fact]
        public void ParseBinary_Uint16BE_ReadsCorrectly()
        {
            var def = BinDef("T={T}", Rule("T", 0, 2, "uint16_be"));
            var bytes = new byte[] { 0x03, 0xE8 };  // 0x03E8 = 1000 big-endian
            var result = new MaskParser(def).ParseBinary(bytes);

            Assert.Equal("T=1000", result.Output);
        }

        // ── int32_le ──────────────────────────────────────────────────────────

        [Fact]
        public void ParseBinary_Int32LE_ReadsNegativeCorrectly()
        {
            var def = BinDef("V={V}", Rule("V", 0, 4, "int32_le"));
            var bytes = BitConverter.GetBytes(-1);
            var result = new MaskParser(def).ParseBinary(bytes);

            Assert.Equal("V=-1", result.Output);
        }

        // ── hex ───────────────────────────────────────────────────────────────

        [Fact]
        public void ParseBinary_Hex_FormatsCorrectly()
        {
            var def = BinDef("RAW={RAW}", Rule("RAW", 0, 3, "hex"));
            var result = new MaskParser(def).ParseBinary(new byte[] { 0xDE, 0xAD, 0xBE });

            Assert.Equal("RAW=DEADBE", result.Output);
        }

        // ── 多欄位 + offset ───────────────────────────────────────────────────

        [Fact]
        public void ParseBinary_MultipleFields_CorrectOffsets()
        {
            var def = BinDef("ID={ID};TEMP={TEMP}",
                Rule("ID",   0, 1, "uint8"),
                Rule("TEMP", 1, 2, "uint16_le"));

            var bytes = new byte[] { 0x02, 0xE8, 0x03 };  // ID=2, TEMP=1000
            var result = new MaskParser(def).ParseBinary(bytes);

            Assert.Equal("ID=2;TEMP=1000", result.Output);
        }

        // ── 超出邊界應忽略，不崩潰 ────────────────────────────────────────────

        [Fact]
        public void ParseBinary_OutOfBoundsField_IsIgnored()
        {
            var def = BinDef("A={A};B={B}",
                Rule("A", 0, 1, "uint8"),
                Rule("B", 5, 2, "uint16_le"));  // 超出 3 bytes 的陣列

            var result = new MaskParser(def).ParseBinary(new byte[] { 0x01, 0x00, 0x00 });

            // B 缺失 → template 有未填佔位符 → 輸出為空
            Assert.Equal(string.Empty, result.Output);
        }

        // ── rawTemplate 回傳 hex dump ──────────────────────────────────────────

        [Fact]
        public void ParseBinary_RawTemplate_ReturnsHexDump()
        {
            var def = BinDef("{raw}");
            var result = new MaskParser(def).ParseBinary(new byte[] { 0xAB, 0xCD });

            Assert.Equal("ABCD", result.Output);
        }
    }
}
