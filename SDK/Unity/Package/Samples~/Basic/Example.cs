using UnityEngine;

public class Example : MonoBehaviour
{
    public GameObject edgeLinkObject;

    EdgeLinkManager edgeLink;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        while (edgeLink.TryDequeue(out string msg))
        {
            Debug.Log("收到：" + msg);
            Debug.Log("溫度：" + edgeLink.Get("temp"));
        }
    }
}
