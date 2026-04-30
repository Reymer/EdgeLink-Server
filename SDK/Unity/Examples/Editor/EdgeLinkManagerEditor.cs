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
    private string[] _maskIds     = null;
    private int      _selectedIdx = 0;
    private string   _statusMsg   = "";
    private bool     _isFetching  = false;

    private static readonly HttpClient _http = CreateHttpClient();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("遮罩瀏覽工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("從 Server 拉取遮罩清單，選取後自動填入 Mask ID 欄位。執行時會自動拉取完整遮罩定義。", MessageType.Info);

        var manager = (EdgeLinkManager)target;

        using (new EditorGUI.DisabledScope(_isFetching))
        {
            if (GUILayout.Button(_isFetching ? "載入中..." : "拉取遮罩清單"))
                _ = FetchMasksAsync(manager.serverUrl, manager.password);
        }

        if (!string.IsNullOrEmpty(_statusMsg))
            EditorGUILayout.HelpBox(_statusMsg, MessageType.None);

        if (_maskIds != null && _maskIds.Length > 0)
        {
            EditorGUILayout.Space(4);
            _selectedIdx = EditorGUILayout.Popup("選擇遮罩", _selectedIdx, _maskIds);
            if (GUILayout.Button("套用 Mask ID"))
            {
                Undo.RecordObject(manager, "Set EdgeLink Mask ID");
                manager.maskId = _maskIds[_selectedIdx];
                EditorUtility.SetDirty(manager);
                _statusMsg = $"Mask ID 已設為：{manager.maskId}";
                Repaint();
            }
        }
    }

    private async Task FetchMasksAsync(string serverUrl, string password)
    {
        _isFetching = true;
        _statusMsg  = "";
        Repaint();
        try
        {
            if (!await LoginAsync(serverUrl, password)) { _statusMsg = "登入失敗，請確認 Server URL 與密碼。"; return; }
            var resp = await _http.GetAsync($"{serverUrl.TrimEnd('/')}/api/masks");
            resp.EnsureSuccessStatusCode();
            var parsed = JsonUtility.FromJson<MaskListResponse>(await resp.Content.ReadAsStringAsync());
            _maskIds    = parsed?.maskTypes ?? Array.Empty<string>();
            _selectedIdx = 0;
            _statusMsg  = $"共找到 {_maskIds.Length} 個遮罩";
        }
        catch (Exception ex) { _statusMsg = $"錯誤：{ex.Message}"; }
        finally { _isFetching = false; Repaint(); }
    }

    private async Task<bool> LoginAsync(string serverUrl, string password)
    {
        string body    = $"{{\"password\":\"{EscapeJson(password)}\"}}";
        var    content = new StringContent(body, Encoding.UTF8, "application/json");
        var    resp    = await _http.PostAsync($"{serverUrl.TrimEnd('/')}/api/auth/login", content);
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
