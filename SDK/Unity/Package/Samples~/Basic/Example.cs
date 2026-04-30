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
        string temp = edgeLink.Get("temp");
        if (temp != null)
            Debug.Log("溫度：" + temp);
    }
}
