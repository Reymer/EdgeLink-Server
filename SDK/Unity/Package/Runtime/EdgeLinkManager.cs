using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using EdgeLink;

public class EdgeLinkManager : MonoBehaviour
{
    public enum Protocol { TCP, TCPListener, UDP }

    [Header("Server")]
    public string   serverUrl = "https://192.168.1.100:8443";
    public string   password  = "";
    public string   maskId    = "OriginalData";

    [Header("連線")]
    public Protocol protocol      = Protocol.TCP;
    public string   tcpHost       = "192.168.1.100";
    public int      tcpPort       = 9001;
    public int      tcpListenPort = 9001;
    public int      udpLocalPort  = 9002;

    [Header("設備偵測")]
    [Tooltip("訊息中代表設備 ID 的欄位名稱，留空則不追蹤 timeout")]
    public string deviceIdKey          = "id";
    [Tooltip("超過幾秒沒收到訊息視為設備離線（0 = 停用）")]
    public float  deviceTimeoutSeconds = 20f;

    [HideInInspector] public string fieldDelimiter = ";";
    [HideInInspector] public string kvSeparator    = ":";

    // ── 狀態 ────────────────────────────────────────────────
    public string Raw { get; private set; }
    public string Get(string key) => _latest.TryGetValue(key, out var v) ? v : null;

    // ── 事件（Unity 主執行緒觸發）────────────────────────────
    /// <summary>每筆新訊息到達時觸發。用 Get(key) 取解析後的欄位值。</summary>
    public event Action<string>       OnMessage;
    /// <summary>上游裝置 TCP 連線 / 斷線時觸發。bool = 是否連線，string = endpoint。</summary>
    public event Action<bool, string> OnDeviceStatus;
    /// <summary>裝置超過 deviceTimeoutSeconds 沒有傳資料時觸發。</summary>
    public event Action<string>       OnDeviceTimeout;
    /// <summary>逾時的裝置重新送資料時觸發。</summary>
    public event Action<string>       OnDeviceReconnected;

    // ── 內部 ────────────────────────────────────────────────
    private EdgeLinkClient      _tcp;
    private EdgeLinkTcpListener _tcpListener;
    private EdgeLinkUdpClient   _udp;

    private readonly Dictionary<string, string>      _latest        = new();
    private readonly Dictionary<string, float>       _lastSeenTime  = new();
    private readonly HashSet<string>                 _timedOut      = new();
    private readonly ConcurrentQueue<(bool, string)> _deviceStatusQ = new();

    // ── 生命週期 ─────────────────────────────────────────────

    private IEnumerator Start()
    {
        yield return FetchMaskCoroutine();
        Connect();
    }

    private void Update()
    {
        if (_tcp         != null) while (_tcp.TryDequeue(out var m))         Handle(m);
        if (_tcpListener != null) while (_tcpListener.TryDequeue(out var m)) Handle(m);
        if (_udp         != null) while (_udp.TryDequeue(out var m))         Handle(m);

        while (_deviceStatusQ.TryDequeue(out var ds))
            OnDeviceStatus?.Invoke(ds.Item1, ds.Item2);

        CheckTimeouts();
    }

    private void OnDestroy()
    {
        _tcp?.Dispose();
        _tcpListener?.Dispose();
        _udp?.Dispose();
    }

    // ── Mask 拉取 ────────────────────────────────────────────

    private IEnumerator FetchMaskCoroutine()
    {
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(maskId)) yield break;

        string baseUrl = serverUrl.TrimEnd('/');

        byte[] body = Encoding.UTF8.GetBytes($"{{\"password\":\"{EscapeJson(password)}\"}}");
        using var loginReq = new UnityWebRequest($"{baseUrl}/api/auth/login", "POST");
        loginReq.uploadHandler   = new UploadHandlerRaw(body);
        loginReq.downloadHandler = new DownloadHandlerBuffer();
        loginReq.SetRequestHeader("Content-Type", "application/json");
        loginReq.certificateHandler = new BypassCertificate();
        yield return loginReq.SendWebRequest();
        if (loginReq.result != UnityWebRequest.Result.Success) yield break;

        string cookie = loginReq.GetResponseHeader("Set-Cookie")?.Split(';')[0] ?? "";

        using var maskReq = UnityWebRequest.Get($"{baseUrl}/api/masks/{Uri.EscapeDataString(maskId)}");
        maskReq.SetRequestHeader("Cookie", cookie);
        maskReq.certificateHandler = new BypassCertificate();
        yield return maskReq.SendWebRequest();
        if (maskReq.result != UnityWebRequest.Result.Success) yield break;

        var def = JsonUtility.FromJson<MaskDefResponse>(maskReq.downloadHandler.text);
        if (def != null)
        {
            if (!string.IsNullOrEmpty(def.fieldDelimiter)) fieldDelimiter = def.fieldDelimiter;
            if (!string.IsNullOrEmpty(def.kvSeparator))    kvSeparator    = def.kvSeparator;
            Debug.Log($"[EdgeLink] 遮罩已套用: {maskId}");
        }
    }

    // ── 建立連線 ─────────────────────────────────────────────

    private async void Connect()
    {
        switch (protocol)
        {
            case Protocol.TCP:
                _tcp = new EdgeLinkClient(tcpHost, tcpPort);
                _tcp.OnConnected    += () => Debug.Log("[EdgeLink TCP] Connected");
                _tcp.OnDisconnected += () => Debug.Log("[EdgeLink TCP] Disconnected");
                _tcp.OnError        += ex => Debug.LogWarning($"[EdgeLink TCP] {ex.Message}");
                _tcp.OnDeviceStatus += (c, ep) => _deviceStatusQ.Enqueue((c, ep));
                _tcp.SetAutoReconnect(true, 5000);
                try   { await _tcp.ConnectAsync(); }
                catch { Debug.LogWarning("[EdgeLink TCP] 初始連線失敗，將自動重試"); }
                break;

            case Protocol.TCPListener:
                _tcpListener = new EdgeLinkTcpListener(tcpListenPort);
                _tcpListener.OnConnected    += () => Debug.Log("[EdgeLink TCPListener] EdgeLink connected");
                _tcpListener.OnDisconnected += () => Debug.Log("[EdgeLink TCPListener] EdgeLink disconnected");
                _tcpListener.OnError        += ex => Debug.LogWarning($"[EdgeLink TCPListener] {ex.Message}");
                _tcpListener.OnDeviceStatus += (c, ep) => _deviceStatusQ.Enqueue((c, ep));
                _tcpListener.Start();
                Debug.Log($"[EdgeLink TCPListener] Listening on port {tcpListenPort}");
                break;

            case Protocol.UDP:
                _udp = new EdgeLinkUdpClient(udpLocalPort);
                _udp.OnError += ex => Debug.LogWarning($"[EdgeLink UDP] {ex.Message}");
                _udp.Start();
                Debug.Log($"[EdgeLink UDP] Listening on port {udpLocalPort}");
                break;
        }
    }

    // ── 訊息處理 ─────────────────────────────────────────────

    private void Handle(string msg)
    {
        Raw = msg;
        var parsed = Parse(msg);
        foreach (var kv in parsed) _latest[kv.Key] = kv.Value;
        OnMessage?.Invoke(msg);

        if (!string.IsNullOrEmpty(deviceIdKey) &&
            parsed.TryGetValue(deviceIdKey, out var deviceId))
        {
            _lastSeenTime[deviceId] = Time.time;
            if (_timedOut.Remove(deviceId))
                OnDeviceReconnected?.Invoke(deviceId);
        }
    }

    private void CheckTimeouts()
    {
        if (deviceTimeoutSeconds <= 0 || string.IsNullOrEmpty(deviceIdKey)) return;
        foreach (var kv in _lastSeenTime)
        {
            if (Time.time - kv.Value > deviceTimeoutSeconds && _timedOut.Add(kv.Key))
                OnDeviceTimeout?.Invoke(kv.Key);
        }
    }

    private Dictionary<string, string> Parse(string msg)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(fieldDelimiter) || string.IsNullOrEmpty(kvSeparator))
        {
            result["raw"] = msg;
            return result;
        }
        foreach (var part in msg.Split(fieldDelimiter, StringSplitOptions.RemoveEmptyEntries))
        {
            int i = part.IndexOf(kvSeparator, StringComparison.Ordinal);
            if (i < 0) continue;
            result[part[..i].Trim()] = part[(i + kvSeparator.Length)..].Trim();
        }
        return result;
    }

    private static string EscapeJson(string s) =>
        s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

    private class BypassCertificate : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }

    [Serializable]
    private class MaskDefResponse
    {
        public string maskId;
        public string outputTemplate;
        public string fieldDelimiter;
        public string kvSeparator;
    }
}
