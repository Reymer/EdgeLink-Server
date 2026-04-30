using UnityEngine;

/// <summary>
/// TCP Client 模式：Unity 主動連線到 EdgeLink Server 的 TCP Server Port
/// EdgeLink Server Port 設定：TCP Server，監聽某個 Port（例如 8888）
/// Inspector 設定：Protocol = TCP，Host = Server IP，Port = 8888
/// </summary>
public class Example_TCP : MonoBehaviour
{
    public GameObject edgeLinkObject;

    EdgeLinkManager edgeLink;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        string temp = edgeLink.Get("temp");
        if (temp != null)
            Debug.Log("溫度：" + temp);
    }
}
