using NUnit.Framework;
using System;

public class MaskProcessorBinaryTests
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

    [Test]
    public void Process_Uint8_ReadsCorrectly()
    {
        var result = MaskProcessor.Process(BinDef("VAL={VAL}", Rule("VAL", 0, 1, "uint8")),
            new byte[] { 0xFF }, null);
        Assert.AreEqual("VAL=255", result);
    }

    [Test]
    public void Process_Uint16LE_ReadsCorrectly()
    {
        var bytes = BitConverter.GetBytes((ushort)1000);
        var result = MaskProcessor.Process(BinDef("T={T}", Rule("T", 0, 2, "uint16_le")),
            bytes, null);
        Assert.AreEqual("T=1000", result);
    }

    [Test]
    public void Process_Uint16BE_ReadsCorrectly()
    {
        var result = MaskProcessor.Process(BinDef("T={T}", Rule("T", 0, 2, "uint16_be")),
            new byte[] { 0x03, 0xE8 }, null);
        Assert.AreEqual("T=1000", result);
    }

    [Test]
    public void Process_Int32LE_NegativeValue()
    {
        var bytes = BitConverter.GetBytes(-1);
        var result = MaskProcessor.Process(BinDef("V={V}", Rule("V", 0, 4, "int32_le")),
            bytes, null);
        Assert.AreEqual("V=-1", result);
    }

    [Test]
    public void Process_Hex_FormatsCorrectly()
    {
        var result = MaskProcessor.Process(BinDef("RAW={RAW}", Rule("RAW", 0, 3, "hex")),
            new byte[] { 0xDE, 0xAD, 0xBE }, null);
        Assert.AreEqual("RAW=DEADBE", result);
    }

    [Test]
    public void Process_MultipleFields_CorrectOffsets()
    {
        var def = BinDef("ID={ID};TEMP={TEMP}",
            Rule("ID",   0, 1, "uint8"),
            Rule("TEMP", 1, 2, "uint16_le"));
        var bytes = new byte[] { 0x02, 0xE8, 0x03 };  // ID=2, TEMP=1000

        var result = MaskProcessor.Process(def, bytes, null);
        Assert.AreEqual("ID=2;TEMP=1000", result);
    }

    [Test]
    public void Process_OutOfBoundsField_ReturnsEmpty()
    {
        var def = BinDef("A={A};B={B}",
            Rule("A", 0, 1, "uint8"),
            Rule("B", 5, 2, "uint16_le"));  // 超出陣列長度

        var result = MaskProcessor.Process(def, new byte[] { 0x01, 0x00, 0x00 }, null);
        Assert.AreEqual(string.Empty, result);
    }

    [Test]
    public void Process_RawTemplate_ReturnsBinaryMessage()
    {
        var def = new MaskDefinition { maskId = "raw", inputEncoding = "binary", outputTemplate = "{raw}" };
        var result = MaskProcessor.Process(def, new byte[] { 0xAB }, "fallback");

        // {raw} → textMessage そのまま返す
        Assert.AreEqual("fallback", result);
    }
}
