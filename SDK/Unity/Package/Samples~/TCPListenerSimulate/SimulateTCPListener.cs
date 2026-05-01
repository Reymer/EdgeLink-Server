using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TCPListener 模式模擬情境 — Unity 端
///
/// 對應情境：
///   FakeArduino ──TCP──▶ EdgeLink Port 8888 (TCP Server)
///                              ↓ 路由轉發
///                         EdgeLink Port 9001 (TCP Client) ──TCP──▶ Unity（此腳本）
///
/// 設定步驟：
///   1. 在此 GameObject 上加入 EdgeLinkManager 組件
///   2. Inspector 設定：
///        Protocol       = TCPListener
///        Listen Port    = 9001
///        Device Id Key  = id
///        Device Timeout = 20
///   3. EdgeLink Web UI 新增：
///        TCP Server Port 8888（等 Arduino 連入）
///        TCP Client Port 9001，Host = Unity 主機 IP，SourceProtocolId = 8888 ID
///   4. 執行 FakeArduino（SDK/CSharp/examples/Simulate_TCPListener/FakeArduino）
/// </summary>
public class SimulateTCPListener : MonoBehaviour
{
    EdgeLinkManager edgeLink;
    string          lastRaw;

    // 顯示用
    readonly List<string> log         = new();
    readonly Dictionary<string, DeviceState> devices = new();
    const int MAX_LOG = 12;

    class DeviceState
    {
        public string Id;
        public string Temp;
        public string Humid;
        public string Seq;
        public bool   IsOnline = true;
        public float  LastSeen;
    }

    void Start()
    {
        edgeLink = GetComponent<EdgeLinkManager>();
        if (edgeLink == null)
        {
            Debug.LogError("[Simulate] 找不到 EdgeLinkManager，請將此腳本與 EdgeLinkManager 掛在同一個 GameObject");
            return;
        }

        // ── EdgeLink ↔ Unity 連線狀態 ──────────────────────────────────────────
        // 這代表 EdgeLink Server 本身是否連入 Unity
        // （TCPListener 模式下，EdgeLink 是主動連進來的那方）

        // ── 上游設備（ESP32）連線 / 斷線（TCP，拔電後約 15 秒偵測到）──────────
        edgeLink.OnDeviceStatus += (connected, endpoint) =>
        {
            string ip = endpoint.Contains("@") ? endpoint.Split('@')[1] : endpoint;
            if (connected)
                AddLog($"<color=#55ff55>▲ 設備上線  IP: {ip}</color>");
            else
                AddLog($"<color=#ff5555>▼ 設備斷線  IP: {ip}</color>");
        };

        // ── Timeout 偵測（TCP/UDP 通用，依 id 欄位計時）──────────────────────
        edgeLink.OnDeviceTimeout += deviceId =>
        {
            AddLog($"<color=#ffaa00>⚠ {deviceId} 超時，可能已離線</color>");
            if (devices.TryGetValue(deviceId, out var d)) d.IsOnline = false;
        };

        // ── 超時設備重新傳資料 ────────────────────────────────────────────────
        edgeLink.OnDeviceReconnected += deviceId =>
        {
            AddLog($"<color=#55ffff>↑ {deviceId} 重新上線</color>");
            if (devices.TryGetValue(deviceId, out var d)) d.IsOnline = true;
        };
    }

    void Update()
    {
        if (edgeLink == null || edgeLink.Raw == lastRaw) return;
        lastRaw = edgeLink.Raw;

        string id    = edgeLink.Get("id");
        string temp  = edgeLink.Get("temp");
        string humid = edgeLink.Get("humid");
        string seq   = edgeLink.Get("seq");

        if (id == null) return;

        if (!devices.TryGetValue(id, out var device))
        {
            device = new DeviceState { Id = id };
            devices[id] = device;
        }

        device.Temp     = temp;
        device.Humid    = humid;
        device.Seq      = seq;
        device.IsOnline = true;
        device.LastSeen = Time.time;

        AddLog($"[{id}] seq={seq} temp={temp}°C humid={humid}%");
    }

    void AddLog(string msg)
    {
        log.Add($"[{System.DateTime.Now:HH:mm:ss}] {msg}");
        if (log.Count > MAX_LOG) log.RemoveAt(0);
    }

    // ── 畫面顯示（不需要額外 UI 物件）──────────────────────────────────────
    void OnGUI()
    {
        GUIStyle bg = new GUIStyle(GUI.skin.box)
        {
            fontSize  = 14,
            alignment = TextAnchor.UpperLeft,
            richText  = true,
            wordWrap  = false
        };

        // 設備狀態面板
        GUILayout.BeginArea(new Rect(10, 10, 380, 200), "設備狀態", GUI.skin.window);
        if (devices.Count == 0)
        {
            GUILayout.Label("尚未收到任何設備資料...");
        }
        else
        {
            foreach (var d in devices.Values)
            {
                string color  = d.IsOnline ? "#55ff55" : "#ff5555";
                string status = d.IsOnline ? "● 在線" : "○ 離線";
                GUILayout.Label(
                    $"<color={color}>{status}</color>  " +
                    $"<b>{d.Id}</b>  " +
                    $"seq={d.Seq}  temp={d.Temp}°C  humid={d.Humid}%",
                    bg);
            }
        }
        GUILayout.EndArea();

        // 事件 log 面板
        GUILayout.BeginArea(new Rect(10, 220, 500, 230), "事件記錄", GUI.skin.window);
        foreach (var entry in log)
            GUILayout.Label(entry, bg);
        GUILayout.EndArea();
    }
}
