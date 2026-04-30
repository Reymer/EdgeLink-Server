using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using EdgeLink;

public class EdgeLinkManager : MonoBehaviour
{
    public enum Protocol { TCP, TCPListener, UDP }

    [Header("EdgeLink Server")]
    public string serverUrl = "https://192.168.1.100:8443";
    public string password  = "";
    public string maskId    = "OriginalData";

    [Header("連線協定")]
    public Protocol protocol = Protocol.TCP;

    [Header("TCP 設定（EdgeLink Server 為 TCP Server 時）")]
    public string tcpHost = "192.168.1.100";
    public int    tcpPort = 9001;

    [Header("TCP Listener 設定（EdgeLink Server 為 TCP Client 時）")]
    public int tcpListenPort = 9001;

    [Header("UDP 設定")]
    public int udpLocalPort = 9002;

    [HideInInspector] public string fieldDelimiter = ";";
    [HideInInspector] public string kvSeparator    = ":";
    [HideInInspector] public string outputTemplate = "{raw}";

    [Header("事件")]
    public UnityEvent<string>                     onRawMessage;
    public UnityEvent<Dictionary<string, string>> onParsedMessage;

    private EdgeLinkClient      _tcp;
    private EdgeLinkTcpListener _tcpListener;
    private EdgeLinkUdpClient   _udp;

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

        // 登入
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

        string rawCookie = loginReq.GetResponseHeader("Set-Cookie");
        string sessionCookie = rawCookie?.Split(';')[0] ?? "";

        // 拉遮罩
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
            _tcp = new EdgeLinkClient(tcpHost, tcpPort);
            _tcp.OnConnected    += () => Debug.Log("[EdgeLink TCP] Connected");
            _tcp.OnDisconnected += () => Debug.Log("[EdgeLink TCP] Disconnected");
            _tcp.OnError        += ex => Debug.LogWarning($"[EdgeLink TCP] {ex.Message}");
            _tcp.SetAutoReconnect(true, 5000);
            try   { await _tcp.ConnectAsync(); }
            catch { Debug.LogWarning("[EdgeLink TCP] Initial connect failed, will retry..."); }
        }
        else if (protocol == Protocol.TCPListener)
        {
            _tcpListener = new EdgeLinkTcpListener(tcpListenPort);
            _tcpListener.OnConnected    += () => Debug.Log("[EdgeLink TCPListener] EdgeLink connected");
            _tcpListener.OnDisconnected += () => Debug.Log("[EdgeLink TCPListener] EdgeLink disconnected");
            _tcpListener.OnError        += ex => Debug.LogWarning($"[EdgeLink TCPListener] {ex.Message}");
            _tcpListener.Start();
            Debug.Log($"[EdgeLink TCPListener] Listening on port {tcpListenPort}");
        }
        else
        {
            _udp = new EdgeLinkUdpClient(udpLocalPort);
            _udp.OnError += ex => Debug.LogWarning($"[EdgeLink UDP] {ex.Message}");
            _udp.Start();
            Debug.Log($"[EdgeLink UDP] Listening on port {udpLocalPort}");
        }
    }

    private void Update()
    {
        if (_tcp != null)
            while (_tcp.TryDequeue(out string msg)) Handle(msg);

        if (_tcpListener != null)
            while (_tcpListener.TryDequeue(out string msg)) Handle(msg);

        if (_udp != null)
            while (_udp.TryDequeue(out string msg)) Handle(msg);
    }

    private void Handle(string msg)
    {
        onRawMessage?.Invoke(msg);
        onParsedMessage?.Invoke(Parse(msg));
    }

    private void OnDestroy()
    {
        _tcp?.Dispose();
        _tcpListener?.Dispose();
        _udp?.Dispose();
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
