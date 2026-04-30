using UnityEngine;

/// <summary>
/// UDP 模式：EdgeLink Server 廣播 UDP 封包到 Unity
/// EdgeLink Server Port 設定：UDP，廣播目標 Port = 9002
/// Inspector 設定：Protocol = UDP，Local Port = 9002
/// </summary>
public class Example_UDP : MonoBehaviour
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
