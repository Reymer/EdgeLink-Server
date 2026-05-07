using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 多設備斷線偵測：追蹤多台裝置的上線 / 離線狀態。
///
/// 兩種斷線機制：
///   OnDeviceStatus   — EdgeLink 偵測到 TCP 連線中斷（拔電約 15 秒）
///   OnDeviceTimeout  — 超過 Device Timeout 秒沒收到該裝置的訊息
///
/// Inspector 設定：
///   EdgeLinkManager → Device Id Key = "id"，Device Timeout = 20
/// </summary>
public class MultiDeviceExample : MonoBehaviour
{
    EdgeLinkManager edgeLink;

    readonly Dictionary<string, bool> deviceOnline = new();

    void Start()
    {
        edgeLink = GetComponent<EdgeLinkManager>();

        // TCP 連線層斷線（拔電 ~15 秒後觸發）
        edgeLink.OnDeviceStatus += (connected, endpoint) =>
        {
            string ip = endpoint.Contains("@") ? endpoint.Split('@')[1] : endpoint;
            if (connected)
                Debug.Log($"[連線] 設備上線  IP={ip}");
            else
                Debug.LogWarning($"[連線] 設備斷線  IP={ip}");
        };

        // 訊息層 Timeout（任何協定皆支援）
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

        // 收到新訊息時更新各裝置最新數值
        edgeLink.OnMessage += _ =>
        {
            string id = edgeLink.Get("id");
            if (id == null) return;

            if (!deviceOnline.ContainsKey(id))
            {
                deviceOnline[id] = true;
                Debug.Log($"[新裝置] {id} 首次上線");
            }

            Debug.Log($"[資料] {id}  temp={edgeLink.Get("temp")}  humidity={edgeLink.Get("humidity")}");
        };
    }
}
