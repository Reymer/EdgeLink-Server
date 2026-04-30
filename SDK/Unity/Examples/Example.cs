using UnityEngine;

public class Example : MonoBehaviour
{
    public GameObject edgeLinkObject;

    EdgeLinkManager edgeLink;
    string          lastRaw;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        if (edgeLink.Raw == lastRaw) return;
        lastRaw = edgeLink.Raw;

        string temp   = edgeLink.Get("temp");
        string humid  = edgeLink.Get("humid");
        string status = edgeLink.Get("status");

        Debug.Log($"溫度:{temp} 濕度:{humid} 狀態:{status}");
    }
}
