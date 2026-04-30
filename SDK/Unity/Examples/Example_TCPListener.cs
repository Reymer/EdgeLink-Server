using UnityEngine;

/// <summary>
/// TCP Listener 模式：EdgeLink Server 主動推資料到 Unity
/// EdgeLink Server Port 設定：TCP Client，目標 IP = Unity 裝置 IP，目標 Port = 9001
/// Inspector 設定：Protocol = TCPListener，Local Port = 9001
/// </summary>
public class Example_TCPListener : MonoBehaviour
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
