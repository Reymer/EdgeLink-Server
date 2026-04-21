using NUnit.Framework;
using System.Text;

public class MaskProcessorTextTests
{
    private static MaskDefinition Def(string template, string fieldDelim = ";", string kvSep = ":") =>
        new MaskDefinition
        {
            maskId = "test",
            inputEncoding = "text",
            fieldDelimiter = fieldDelim,
            kvSeparator = kvSep,
            outputTemplate = template,
        };

    // ── 正向解析 ──────────────────────────────────────────────────────────────

    [Test]
    public void Process_ExtractsFieldsAndFillsTemplate()
    {
        var result = MaskProcessor.Process(Def("ID={ID};TEMP={TEMP}"), null, "ID:2;TEMP:5");
        Assert.AreEqual("ID=2;TEMP=5", result);
    }

    [Test]
    public void Process_RawTemplate_ReturnsMessageAsIs()
    {
        var result = MaskProcessor.Process(Def("{raw}"), null, "ID:2;TEMP:5");
        Assert.AreEqual("ID:2;TEMP:5", result);
    }

    [Test]
    public void Process_MissingField_ReturnsEmptyString()
    {
        var result = MaskProcessor.Process(Def("ID={ID};TEMP={TEMP}"), null, "ID:2");
        Assert.AreEqual(string.Empty, result);
    }

    [Test]
    public void Process_NullDef_ReturnsRawMessage()
    {
        var result = MaskProcessor.Process(null, null, "raw_message");
        Assert.AreEqual("raw_message", result);
    }

    [Test]
    public void Process_EmptyMessage_ReturnsEmptyString()
    {
        var result = MaskProcessor.Process(Def("ID={ID}"), null, string.Empty);
        Assert.AreEqual(string.Empty, result);
    }

    [Test]
    public void Process_CustomDelimiters()
    {
        var result = MaskProcessor.Process(
            Def("ID={ID},TEMP={TEMP}", fieldDelim: ",", kvSep: "="),
            null, "ID=10,TEMP=25");
        Assert.AreEqual("ID=10,TEMP=25", result);
    }
}
