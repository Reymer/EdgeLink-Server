using UnityEngine;

/// <summary>
/// TCPListener 模式：Unity 開 Port 等待 EdgeLink Server 主動連入。
/// 適用場景：Server 扮演 TCP Client，把收到的裝置資料推送給 Unity。
///
/// Inspector 設定：
///   EdgeLinkManager → Protocol = TCPListener，Local Port = 9001
/// </summary>
public class TCPListenerExample : MonoBehaviour
{
    EdgeLinkManager edgeLink;

    void Start()
    {
        edgeLink = GetComponent<EdgeLinkManager>();

        edgeLink.OnMessage += _ =>
        {
            string id   = edgeLink.Get("id");
            string temp = edgeLink.Get("temp");
            Debug.Log($"[TCPListener] {id}  temp={temp}");
        };
    }
}
