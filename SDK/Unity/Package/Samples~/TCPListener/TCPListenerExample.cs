using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TCPListener 模式完整範例：Unity 開 Port 等待 EdgeLink Server 主動連入。
///
/// Inspector 設定（EdgeLinkManager）：
///   Protocol        = TCPListener
///   TCP Listen Port = 9001
///   Device Id Key   = "id"      （訊息中代表設備 ID 的欄位）
///   Device Timeout  = 20        （超過幾秒沒訊息視為離線）
///
/// 斷線偵測：
///   OnDeviceStatus   — TCP 連線層斷線（裝置拔電後約 15 秒由 Server PING/PONG 偵測到）
///   OnDeviceTimeout  — 訊息層逾時（超過 Device Timeout 秒沒收到該 id 的資料）
/// </summary>
public class TCPListenerExample : MonoBehaviour
{
    EdgeLinkManager edgeLink;

    readonly Dictionary<string, bool> deviceOnline = new();

    void Start()
    {
        edgeLink = GetComponent<EdgeLinkManager>();

        // EdgeLink Server 本身連入 / 離開
        // （不是裝置，而是 Server 這條 TCP 連線的狀態）

        // 裝置 TCP 連線層斷線（由 Server PING/PONG 偵測，拔電約 15 秒後觸發）
        // deviceId 在該裝置送過至少一筆資料後才有值，第一次連線時為空字串
        edgeLink.OnDeviceStatus += (connected, endpoint, deviceId) =>
        {
            string ip    = endpoint.Contains("@") ? endpoint.Split('@')[1] : endpoint;
            string label = string.IsNullOrEmpty(deviceId) ? ip : deviceId;
            if (connected)
                Debug.Log($"[連線] 設備上線  {label}");
            else
                Debug.LogWarning($"[連線] 設備斷線  {label}");
        };

        // 訊息層逾時（任何協定皆可用，以 id 欄位識別裝置）
        edgeLink.OnDeviceTimeout += id =>
        {
            deviceOnline[id] = false;
            Debug.LogWarning($"[Timeout] {id} 超過 {edgeLink.deviceTimeoutSeconds}s 沒有資料，視為離線");
        };

        edgeLink.OnDeviceReconnected += id =>
        {
            deviceOnline[id] = true;
            Debug.Log($"[Timeout] {id} 重新上線");
        };

        // 每筆新訊息到達
        edgeLink.OnMessage += _ =>
        {
            string id = edgeLink.Get("id");
            if (id == null) return;

            if (!deviceOnline.ContainsKey(id))
            {
                deviceOnline[id] = true;
                Debug.Log($"[新裝置] {id} 首次上線");
            }

            string temp     = edgeLink.Get("temp");
            string humidity = edgeLink.Get("humidity");
            Debug.Log($"[資料] {id}  temp={temp}  humidity={humidity}");
        };
    }
}
