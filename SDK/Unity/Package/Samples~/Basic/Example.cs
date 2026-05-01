using UnityEngine;

/// <summary>
/// EdgeLink 基本範例 — 多設備資料讀取、斷線偵測
///
/// Inspector 設定：
///   edgeLinkObject → 掛有 EdgeLinkManager 的 GameObject
///   deviceIdKey    → 訊息中代表設備 ID 的欄位（預設 "id"）
///   deviceTimeout  → 超過幾秒沒收到資料視為斷線（預設 20 秒）
/// </summary>
public class Example : MonoBehaviour
{
    public GameObject edgeLinkObject;

    EdgeLinkManager edgeLink;
    string          lastRaw;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();

        // TCP 正常斷線 / 拔電後 ~15 秒偵測到（EdgeLink PING/PONG 超時）
        edgeLink.OnDeviceStatus += (connected, endpoint) =>
        {
            string ip = endpoint.Contains("@") ? endpoint.Split('@')[1] : endpoint;
            if (connected)
                Debug.Log($"[EdgeLink] 設備上線  IP: {ip}");
            else
                Debug.LogWarning($"[EdgeLink] 設備斷線  IP: {ip}");
        };

        // Timeout 偵測：超過 deviceTimeoutSeconds 沒收到訊息（TCP / UDP 通用）
        edgeLink.OnDeviceTimeout += deviceId =>
        {
            Debug.LogWarning($"[EdgeLink] 設備 {deviceId} 超時，可能已離線");
        };

        // 超時設備重新送資料
        edgeLink.OnDeviceReconnected += deviceId =>
        {
            Debug.Log($"[EdgeLink] 設備 {deviceId} 重新上線");
        };
    }

    void Update()
    {
        // 只在新訊息到達時處理
        if (edgeLink.Raw == lastRaw) return;
        lastRaw = edgeLink.Raw;

        string id     = edgeLink.Get("id");
        string temp   = edgeLink.Get("temp");
        string humid  = edgeLink.Get("humid");
        string status = edgeLink.Get("status");

        Debug.Log($"[{id}] 溫度:{temp} 濕度:{humid} 狀態:{status}");
    }
}
