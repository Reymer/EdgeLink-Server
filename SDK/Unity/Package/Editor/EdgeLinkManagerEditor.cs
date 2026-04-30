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
    private string _serverUrl    = "https://192.168.1.100:8443";
    private string _password     = "admin";
    private string[] _maskIds    = null;
    private int      _selectedIdx = 0;
    private string   _statusMsg  = "";
    private bool     _isFetching = false;

    private static readonly HttpClient _http = CreateHttpClient();

    public override void OnInspectorGUI()
    {
        var manager = (EdgeLinkManager)target;

        // 協定選擇
        EditorGUI.BeginChangeCheck();
        manager.protocol = (EdgeLinkManager.Protocol)EditorGUILayout.EnumPopup("Protocol", manager.protocol);
        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(manager);

        EditorGUILayout.Space(4);

        // 依協定顯示對應欄位
        if (manager.protocol == EdgeLinkManager.Protocol.TCP)
        {
            manager.tcpHost = EditorGUILayout.TextField("Host", manager.tcpHost);
            manager.tcpPort = EditorGUILayout.IntField("Port", manager.tcpPort);
        }
        else
        {
            manager.udpLocalPort = EditorGUILayout.IntField("Local Port (listen)", manager.udpLocalPort);
        }

        EditorGUILayout.Space(8);

        // 遮罩欄位（唯讀顯示，由下方工具套用）
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("Mask ID",         manager.maskId);
            EditorGUILayout.TextField("Field Delimiter", manager.fieldDelimiter);
            EditorGUILayout.TextField("KV Separator",    manager.kvSeparator);
            EditorGUILayout.TextField("Output Template", manager.outputTemplate);
        }

        // 事件
        EditorGUILayout.Space(4);
        var so = new SerializedObject(manager);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("onRawMessage"));
        EditorGUILayout.PropertyField(so.FindProperty("onParsedMessage"));
        so.ApplyModifiedProperties();

        // ── 遮罩設定工具 ─────────────────────────────────────
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("遮罩設定工具", EditorStyles.boldLabel);

        _serverUrl = EditorGUILayout.TextField("Server URL", _serverUrl);
        _password  = EditorGUILayout.PasswordField("Password",  _password);

        EditorGUILayout.Space(4);

        using (new EditorGUI.DisabledScope(_isFetching))
        {
            if (GUILayout.Button(_isFetching ? "載入中..." : "拉取遮罩清單"))
                _ = FetchMasksAsync();
        }

        if (!string.IsNullOrEmpty(_statusMsg))
            EditorGUILayout.HelpBox(_statusMsg, MessageType.Info);

        if (_maskIds != null && _maskIds.Length > 0)
        {
            EditorGUILayout.Space(4);
            _selectedIdx = EditorGUILayout.Popup("選擇遮罩", _selectedIdx, _maskIds);
            if (GUILayout.Button("套用到 Component"))
                _ = ApplyMaskAsync(_maskIds[_selectedIdx]);
        }
    }

    private async Task FetchMasksAsync()
    {
        _isFetching = true;
        _statusMsg  = "";
        Repaint();
        try
        {
            if (!await LoginAsync()) { _statusMsg = "登入失敗，請確認密碼。"; return; }
            var resp = await _http.GetAsync($"{_serverUrl.TrimEnd('/')}/api/masks");
            resp.EnsureSuccessStatusCode();
            var parsed  = JsonUtility.FromJson<MaskListResponse>(await resp.Content.ReadAsStringAsync());
            _maskIds    = parsed?.maskTypes ?? Array.Empty<string>();
            _selectedIdx = 0;
            _statusMsg  = $"共找到 {_maskIds.Length} 個遮罩";
        }
        catch (Exception ex) { _statusMsg = $"錯誤：{ex.Message}"; }
        finally { _isFetching = false; Repaint(); }
    }

    private async Task ApplyMaskAsync(string maskId)
    {
        _statusMsg = "";
        try
        {
            var resp = await _http.GetAsync(
                $"{_serverUrl.TrimEnd('/')}/api/masks/{Uri.EscapeDataString(maskId)}");
            resp.EnsureSuccessStatusCode();

            var def = JsonUtility.FromJson<MaskDefResponse>(await resp.Content.ReadAsStringAsync());
            if (def == null) { _statusMsg = "解析遮罩定義失敗"; return; }

            var manager = (EdgeLinkManager)target;
            Undo.RecordObject(manager, "Apply EdgeLink Mask");
            manager.maskId         = def.maskId;
            manager.fieldDelimiter = string.IsNullOrEmpty(def.fieldDelimiter) ? ";" : def.fieldDelimiter;
            manager.kvSeparator    = string.IsNullOrEmpty(def.kvSeparator)    ? ":" : def.kvSeparator;
            manager.outputTemplate = def.outputTemplate;
            EditorUtility.SetDirty(manager);
            _statusMsg = $"已套用遮罩：{maskId}";
        }
        catch (Exception ex) { _statusMsg = $"錯誤：{ex.Message}"; }
        Repaint();
    }

    private async Task<bool> LoginAsync()
    {
        string body = $"{{\"password\":\"{EscapeJson(_password)}\"}}";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_serverUrl.TrimEnd('/')}/api/auth/login", content);
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

    [Serializable]
    private class MaskDefResponse
    {
        public string maskId;
        public string outputTemplate;
        public string fieldDelimiter;
        public string kvSeparator;
    }
}
