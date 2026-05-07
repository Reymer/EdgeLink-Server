using UnityEngine;

/// <summary>
/// TCP Client 模式：Unity 主動連到 EdgeLink Server 的 TCP Server Port。
/// 適用場景：Unity 需要主動發起連線、Server 同時服務多個 client。
/// 斷線後自動重連（預設 5 秒）。
///
/// Inspector 設定：
///   EdgeLinkManager → Protocol = TCP，TCP Host = Server IP，TCP Port = 9001
/// </summary>
public class TCPClientExample : MonoBehaviour
{
    EdgeLinkManager edgeLink;

    void Start()
    {
        edgeLink = GetComponent<EdgeLinkManager>();

        edgeLink.OnMessage += _ =>
        {
            string id   = edgeLink.Get("id");
            string temp = edgeLink.Get("temp");
            Debug.Log($"[TCP] {id}  temp={temp}");
        };
    }
}
