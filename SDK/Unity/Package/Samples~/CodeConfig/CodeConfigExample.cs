using UnityEngine;

/// <summary>
/// 純程式碼設定：在 Awake 覆蓋所有參數，不依賴 Inspector 填值。
/// 適用場景：動態切換環境（開發 / 正式）、從設定檔讀取連線資訊。
///
/// 使用方式：
///   1. 在 GameObject 掛上 EdgeLinkManager（不需填 Inspector 欄位）
///   2. 在同一個或其他 Script 的 Awake 設定參數
///   3. Start() 自動用設定好的值連線
/// </summary>
public class CodeConfigExample : MonoBehaviour
{
    // 可從外部傳入，或從 PlayerPrefs / ScriptableObject / 環境設定讀取
    [SerializeField] string serverUrl = "https://172.20.10.3:8443";
    [SerializeField] string password  = "admin";
    [SerializeField] string maskId    = "dt-json";

    void Awake()
    {
        var edgeLink = GetComponent<EdgeLinkManager>();

        // 覆蓋 Inspector 的值
        edgeLink.serverUrl     = serverUrl;
        edgeLink.password      = password;
        edgeLink.maskId        = maskId;
        edgeLink.protocol      = EdgeLinkManager.Protocol.TCPListener;
        edgeLink.tcpListenPort = 9001;
        edgeLink.deviceIdKey          = "id";
        edgeLink.deviceTimeoutSeconds = 20f;

        // 訂閱事件（在 Start 之前訂閱，不會漏掉任何訊息）
        edgeLink.OnMessage += _ =>
        {
            Debug.Log($"temp={edgeLink.Get("temp")}  humidity={edgeLink.Get("humidity")}");
        };

        edgeLink.OnDeviceTimeout     += id => Debug.LogWarning($"{id} 離線");
        edgeLink.OnDeviceReconnected += id => Debug.Log($"{id} 重新上線");
    }
}
