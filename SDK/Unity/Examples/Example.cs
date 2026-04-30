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
            string temp = edgeLink.Get("temp");
            Debug.Log("溫度：" + temp);
        }
    }
}
