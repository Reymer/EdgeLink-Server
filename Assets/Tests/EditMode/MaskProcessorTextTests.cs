using NUnit.Framework;
using System.Text;

public class MaskProcessorTextTests
{
    private static MaskDefinition Def(string template, string fieldDelim = ";", string kvSep = ":") =>
        new MaskDefinition
        {
            maskId = "test",
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

    // ── 欄位值含大括號（回歸測試：樣板替換 bug fix）────────────────────────────

    [Test]
    public void Process_FieldValueContainsBraces_DoesNotInterferenceOtherPlaceholder()
    {
        // 修復前：a="{b}", b="hello"
        //   StringBuilder.Replace 先把 {a} → {b}，字串變 "{b} {b}"
        //   再把 {b} → hello，結果 "hello hello"（錯誤）
        // 修復後：Regex.Replace + MatchEvaluator 只掃原始樣板一次
        //   {a} → "{b}"，{b} → "hello"，結果 "{b} hello"（正確）
        var result = MaskProcessor.Process(Def("{a} {b}"), null, "a:{b};b:hello");
        Assert.AreEqual("{b} hello", result);
    }

    [Test]
    public void Process_FieldValueContainsOpenBrace_NoCorruption()
    {
        var result = MaskProcessor.Process(Def("VAL={v}"), null, "v:abc{def");
        Assert.AreEqual("VAL=abc{def", result);
    }

    [Test]
    public void Process_FieldValueContainsClosingBrace_NoCorruption()
    {
        var result = MaskProcessor.Process(Def("VAL={v}"), null, "v:abc}def");
        Assert.AreEqual("VAL=abc}def", result);
    }

    [Test]
    public void Process_MultipleFieldsWithBracesInValues_AllReplacedCorrectly()
    {
        var result = MaskProcessor.Process(Def("{x}-{y}-{z}"), null, "x:{y};y:{z};z:end");
        Assert.AreEqual("{y}-{z}-end", result);
    }

    // ── extraFields 注入（Concurrent 模式 correlationId 情境）────────────────────

    [Test]
    public void Process_ExtraFields_AreMergedIntoTemplate()
    {
        var extra = new System.Collections.Generic.Dictionary<string, string>
            { ["_corrId"] = "abc12345" };
        var result = MaskProcessor.Process(
            Def("ID={ID};CID={_corrId}"), null, "ID:99", extra);
        Assert.AreEqual("ID=99;CID=abc12345", result);
    }

    [Test]
    public void Process_ExtraFields_CanOverrideParsedField()
    {
        // extraFields 後蓋前：若欄位名稱重複，extraFields 值勝出
        var extra = new System.Collections.Generic.Dictionary<string, string>
            { ["ID"] = "OVERRIDE" };
        var result = MaskProcessor.Process(Def("ID={ID}"), null, "ID:original", extra);
        Assert.AreEqual("ID=OVERRIDE", result);
    }

    [Test]
    public void Process_ExtraFields_MissingOtherPlaceholder_ReturnsEmpty()
    {
        // extraFields 補了 _corrId，但模板還需要 TEMP 沒有 → 空字串
        var extra = new System.Collections.Generic.Dictionary<string, string>
            { ["_corrId"] = "abc12345" };
        var result = MaskProcessor.Process(
            Def("ID={ID};TEMP={TEMP};CID={_corrId}"), null, "ID:1", extra);
        Assert.AreEqual(string.Empty, result);
    }

    [Test]
    public void Process_ExtraFields_AllPlaceholdersSatisfied_ReturnsResult()
    {
        var extra = new System.Collections.Generic.Dictionary<string, string>
            { ["_corrId"] = "xyz" };
        var result = MaskProcessor.Process(
            Def("T={TEMP};C={_corrId}"), null, "TEMP:36.5", extra);
        Assert.AreEqual("T=36.5;C=xyz", result);
    }

    [Test]
    public void Process_NullExtraFields_DoesNotThrow()
    {
        var result = MaskProcessor.Process(Def("ID={ID}"), null, "ID:1", null);
        Assert.AreEqual("ID=1", result);
    }

    // ── Raw 及邊界 ────────────────────────────────────────────────────────────

    [Test]
    public void Process_RawTemplate_WithExtraFields_StillReturnsRaw()
    {
        // {raw} 模板直接透傳，extraFields 不應影響輸出
        var extra = new System.Collections.Generic.Dictionary<string, string>
            { ["_corrId"] = "abc" };
        var result = MaskProcessor.Process(Def("{raw}"), null, "ID:1;TEMP:25", extra);
        Assert.AreEqual("ID:1;TEMP:25", result);
    }

    [Test]
    public void Process_EmptyTemplate_ReturnsRaw()
    {
        var result = MaskProcessor.Process(Def(""), null, "some_data");
        Assert.AreEqual("some_data", result);
    }

    [Test]
    public void Process_SingleFieldTemplate_ExactMatch()
    {
        var result = MaskProcessor.Process(Def("{TEMP}"), null, "TEMP:99.9");
        Assert.AreEqual("99.9", result);
    }
}
