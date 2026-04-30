using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EdgeLinkManager))]
public class EdgeLinkManagerEditor : Editor
{
    private string[] maskIds     = null;
    private int      selectedIdx = 0;
    private string   statusMsg   = "";
    private bool     isFetching  = false;

    private static readonly HttpClient http = CreateHttpClient();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("遮罩瀏覽工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("從 Server 拉取遮罩清單，選取後自動填入 Mask ID 欄位。執行時會自動拉取完整遮罩定義。", MessageType.Info);

        var manager = (EdgeLinkManager)target;

        using (new EditorGUI.DisabledScope(isFetching))
        {
            if (GUILayout.Button(isFetching ? "載入中..." : "拉取遮罩清單"))
                _ = FetchMasksAsync(manager.serverUrl, manager.password);
        }

        if (!string.IsNullOrEmpty(statusMsg))
            EditorGUILayout.HelpBox(statusMsg, MessageType.None);

        if (maskIds != null && maskIds.Length > 0)
        {
            EditorGUILayout.Space(4);
            selectedIdx = EditorGUILayout.Popup("選擇遮罩", selectedIdx, maskIds);
            if (GUILayout.Button("套用 Mask ID"))
            {
                Undo.RecordObject(manager, "Set EdgeLink Mask ID");
                manager.maskId = maskIds[selectedIdx];
                EditorUtility.SetDirty(manager);
                statusMsg = $"Mask ID 已設為：{manager.maskId}";
                Repaint();
            }
        }
    }

    private async Task FetchMasksAsync(string serverUrl, string password)
    {
        isFetching = true;
        statusMsg  = "";
        Repaint();
        try
        {
            if (!await LoginAsync(serverUrl, password)) { statusMsg = "登入失敗，請確認 Server URL 與密碼。"; return; }
            var resp = await http.GetAsync($"{serverUrl.TrimEnd('/')}/api/masks");
            resp.EnsureSuccessStatusCode();
            var parsed = JsonUtility.FromJson<MaskListResponse>(await resp.Content.ReadAsStringAsync());
            maskIds    = parsed?.maskTypes ?? Array.Empty<string>();
            selectedIdx = 0;
            statusMsg  = $"共找到 {maskIds.Length} 個遮罩";
        }
        catch (Exception ex) { statusMsg = $"錯誤：{ex.Message}"; }
        finally { isFetching = false; Repaint(); }
    }

    private async Task<bool> LoginAsync(string serverUrl, string password)
    {
        string body    = $"{{\"password\":\"{EscapeJson(password)}\"}}";
        var    content = new StringContent(body, Encoding.UTF8, "application/json");
        var    resp    = await http.PostAsync($"{serverUrl.TrimEnd('/')}/api/auth/login", content);
        return resp.IsSuccessStatusCode;
    }

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies      = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        return new HttpClient(handler);
    }

    [Serializable] private class MaskListResponse { public string[] maskTypes; }
}
