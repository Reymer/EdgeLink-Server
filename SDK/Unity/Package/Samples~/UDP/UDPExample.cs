using UnityEngine;

/// <summary>
/// UDP 模式：Unity 監聽指定 Port，EdgeLink Server 以 UDP 轉發裝置資料。
/// 適用場景：不需要可靠連線、低延遲廣播。UDP 沒有 PING/PONG，不支援斷線偵測。
///
/// Inspector 設定：
///   EdgeLinkManager → Protocol = UDP，UDP Local Port = 9002
/// </summary>
public class UDPExample : MonoBehaviour
{
    EdgeLinkManager edgeLink;

    void Start()
    {
        edgeLink = GetComponent<EdgeLinkManager>();

        edgeLink.OnMessage += _ =>
        {
            string id       = edgeLink.Get("id");
            string temp     = edgeLink.Get("temp");
            string humidity = edgeLink.Get("humidity");
            Debug.Log($"[UDP] {id}  temp={temp}  humidity={humidity}");
        };
    }
}
