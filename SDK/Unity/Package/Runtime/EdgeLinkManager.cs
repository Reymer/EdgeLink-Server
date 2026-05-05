using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using EdgeLink;

public class EdgeLinkManager : MonoBehaviour
{
    public enum Protocol { TCP, TCPListener, UDP }

    public string   serverUrl     = "https://192.168.1.100:8443";
    public string   password      = "";
    public string   maskId        = "OriginalData";
    public Protocol protocol      = Protocol.TCP;
    public string   tcpHost       = "192.168.1.100";
    public int      tcpPort       = 9001;
    public int      tcpListenPort = 9001;
    public int      udpLocalPort  = 9002;

    [HideInInspector] public string fieldDelimiter = ";";
    [HideInInspector] public string kvSeparator    = ":";
    [HideInInspector] public string outputTemplate = "{raw}";

    [Tooltip("訊息中代表設備 ID 的欄位名稱，留空則不追蹤 timeout")]
    public string deviceIdKey         = "id";
    [Tooltip("超過幾秒沒收到訊息視為設備離線（0 = 停用）")]
    public float  deviceTimeoutSeconds = 20f;

    private EdgeLinkClient      tcp;
    private EdgeLinkTcpListener tcpListener;
    private EdgeLinkUdpClient   udp;

    private readonly Dictionary<string, string> latest      = new();
    private readonly Dictionary<string, float>  lastSeenTime = new();
    private readonly HashSet<string>             timedOut     = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(bool, string)> deviceStatusQueue = new();

    public string Raw { get; private set; }

    /// <summary>Fired on Unity main thread when an upstream device connects/disconnects (TCP only).
    /// bool = isConnected, string = endpoint (e.g. "TCPServer@192.168.1.50")</summary>
    public event Action<bool, string> OnDeviceStatus;

    /// <summary>Fired on Unity main thread when a device ID stops sending data beyond deviceTimeoutSeconds.</summary>
    public event Action<string> OnDeviceTimeout;

    /// <summary>Fired on Unity main thread when a previously timed-out device sends data again.</summary>
    public event Action<string> OnDeviceReconnected;

    public string Get(string key) =>
        latest.TryGetValue(key, out string val) ? val : null;

    private IEnumerator Start()
    {
        yield return FetchMaskCoroutine();
        StartConnection();
    }

    private IEnumerator FetchMaskCoroutine()
    {
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(maskId))
            yield break;

        string baseUrl = serverUrl.TrimEnd('/');

        byte[] loginBody = Encoding.UTF8.GetBytes($"{{\"password\":\"{EscapeJson(password)}\"}}");
        using var loginReq = new UnityWebRequest($"{baseUrl}/api/auth/login", "POST");
        loginReq.uploadHandler   = new UploadHandlerRaw(loginBody);
        loginReq.downloadHandler = new DownloadHandlerBuffer();
        loginReq.SetRequestHeader("Content-Type", "application/json");
        loginReq.certificateHandler = new BypassCertificate();
        yield return loginReq.SendWebRequest();

        if (loginReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[EdgeLink] 登入失敗: {loginReq.error}，使用預設遮罩設定");
            yield break;
        }

        string rawCookie     = loginReq.GetResponseHeader("Set-Cookie");
        string sessionCookie = rawCookie?.Split(';')[0] ?? "";

        using var maskReq = UnityWebRequest.Get($"{baseUrl}/api/masks/{Uri.EscapeDataString(maskId)}");
        maskReq.SetRequestHeader("Cookie", sessionCookie);
        maskReq.certificateHandler = new BypassCertificate();
        yield return maskReq.SendWebRequest();

        if (maskReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[EdgeLink] 拉取遮罩失敗: {maskReq.error}，使用預設遮罩設定");
            yield break;
        }

        var def = JsonUtility.FromJson<MaskDefResponse>(maskReq.downloadHandler.text);
        if (def != null)
        {
            fieldDelimiter = string.IsNullOrEmpty(def.fieldDelimiter) ? ";" : def.fieldDelimiter;
            kvSeparator    = string.IsNullOrEmpty(def.kvSeparator)    ? ":" : def.kvSeparator;
            outputTemplate = def.outputTemplate ?? "{raw}";
            Debug.Log($"[EdgeLink] 遮罩已套用: {maskId}");
        }
    }

    private async void StartConnection()
    {
        if (protocol == Protocol.TCP)
        {
            tcp = new EdgeLinkClient(tcpHost, tcpPort);
            tcp.OnConnected    += () => Debug.Log("[EdgeLink TCP] Connected");
            tcp.OnDisconnected += () => Debug.Log("[EdgeLink TCP] Disconnected");
            tcp.OnError        += ex => Debug.LogWarning($"[EdgeLink TCP] {ex.Message}");
            tcp.OnDeviceStatus += (connected, ep) => deviceStatusQueue.Enqueue((connected, ep));
            tcp.SetAutoReconnect(true, 5000);
            try   { await tcp.ConnectAsync(); }
            catch { Debug.LogWarning("[EdgeLink TCP] Initial connect failed, will retry..."); }
        }
        else if (protocol == Protocol.TCPListener)
        {
            tcpListener = new EdgeLinkTcpListener(tcpListenPort);
            tcpListener.OnConnected    += () => Debug.Log("[EdgeLink TCPListener] EdgeLink connected");
            tcpListener.OnDisconnected += () => Debug.Log("[EdgeLink TCPListener] EdgeLink disconnected");
            tcpListener.OnError        += ex => Debug.LogWarning($"[EdgeLink TCPListener] {ex.Message}");
            tcpListener.OnDeviceStatus += (connected, ep) => deviceStatusQueue.Enqueue((connected, ep));
            tcpListener.Start();
            Debug.Log($"[EdgeLink TCPListener] Listening on port {tcpListenPort}");
        }
        else
        {
            udp = new EdgeLinkUdpClient(udpLocalPort);
            udp.OnError += ex => Debug.LogWarning($"[EdgeLink UDP] {ex.Message}");
            udp.Start();
            Debug.Log($"[EdgeLink UDP] Listening on port {udpLocalPort}");
        }
    }

    private void Update()
    {
        if (tcp         != null) while (tcp.TryDequeue(out string msg))         Handle(msg);
        if (tcpListener != null) while (tcpListener.TryDequeue(out string msg)) Handle(msg);
        if (udp         != null) while (udp.TryDequeue(out string msg))         Handle(msg);

        while (deviceStatusQueue.TryDequeue(out var ds))
            OnDeviceStatus?.Invoke(ds.Item1, ds.Item2);

        CheckDeviceTimeouts();
    }

    private void CheckDeviceTimeouts()
    {
        if (deviceTimeoutSeconds <= 0 || string.IsNullOrEmpty(deviceIdKey)) return;

        foreach (var kv in lastSeenTime)
        {
            bool isTimedOut = Time.time - kv.Value > deviceTimeoutSeconds;
            if (isTimedOut && !timedOut.Contains(kv.Key))
            {
                timedOut.Add(kv.Key);
                OnDeviceTimeout?.Invoke(kv.Key);
            }
        }
    }

    private void Handle(string msg)
    {
        Raw = msg;
        var parsed = Parse(msg);
        foreach (var kv in parsed) latest[kv.Key] = kv.Value;

        if (!string.IsNullOrEmpty(deviceIdKey) && parsed.TryGetValue(deviceIdKey, out string deviceId))
        {
            lastSeenTime[deviceId] = Time.time;
            if (timedOut.Remove(deviceId))
                OnDeviceReconnected?.Invoke(deviceId);
        }
    }

    private void OnDestroy()
    {
        tcp?.Dispose();
        tcpListener?.Dispose();
        udp?.Dispose();
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
            int i = part.IndexOf(kvSeparator);
            if (i < 0) continue;
            result[part[..i].Trim()] = part[(i + kvSeparator.Length)..].Trim();
        }
        return result;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
