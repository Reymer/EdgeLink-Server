using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using DevKit;
using UnityEngine;

public class LanguageApiHandler
{
    // Must match the order in LanguageTable: zh-TW=0, en-US=1, ja-JP=2
    private static readonly string[] CodeByIndex = { "zh-TW", "en-US", "ja-JP" };

    private static int CodeToIndex(string code) => code switch
    {
        "en-US" => 1,
        "ja-JP" => 2,
        _       => 0   // zh-TW or unknown
    };

    public Task GetAsync(HttpListenerContext ctx)
    {
        int idx = Localization.Instance.GetCurrentLanguageIndex();
        string code = idx >= 0 && idx < CodeByIndex.Length ? CodeByIndex[idx] : "zh-TW";
        HttpApiServer.WriteJson(ctx, 200, $"{{\"languageCode\":\"{code}\",\"index\":{idx}}}");
        return Task.CompletedTask;
    }

    public async Task SetAsync(HttpListenerContext ctx)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        var req = JsonUtility.FromJson<LanguageReq>(body);
        if (req == null || string.IsNullOrEmpty(req.languageCode))
        {
            HttpApiServer.WriteError(ctx, 400, "languageCode is required");
            return;
        }

        int idx = CodeToIndex(req.languageCode);

        await MainThreadTaskDispatcher.RunOnMainThread(() =>
        {
            Localization.Instance.SetCurrentLanguage(idx);
            PlayerPrefs.SetInt("SelectedLanguageIndex", idx);
            PlayerPrefs.Save();
        });

        HttpApiServer.WriteJson(ctx, 200, "{\"success\":true}");
    }
}
