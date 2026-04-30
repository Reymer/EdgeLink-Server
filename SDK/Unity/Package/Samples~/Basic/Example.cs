using UnityEngine;

public class Example : MonoBehaviour
{
    public GameObject edgeLinkObject;

    EdgeLinkManager edgeLink;
    string lastRaw;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        if (edgeLink.Raw == null || edgeLink.Raw == lastRaw) return;
        lastRaw = edgeLink.Raw;

        string temp = edgeLink.Get("temp");
        Debug.Log("溫度：" + temp);
    }
}
