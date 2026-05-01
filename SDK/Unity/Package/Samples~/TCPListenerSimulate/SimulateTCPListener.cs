using UnityEngine;

public class SimulateTCPListener : MonoBehaviour
{
    EdgeLinkManager edgeLink;
    string          lastRaw;

    void Start()
    {
        edgeLink = GetComponent<EdgeLinkManager>();

        edgeLink.OnDeviceStatus      += (ok, ep)  => Debug.Log(ok ? $"設備上線: {ep}" : $"設備斷線: {ep}");
        edgeLink.OnDeviceTimeout     += id         => Debug.LogWarning($"{id} 離線（超時）");
        edgeLink.OnDeviceReconnected += id         => Debug.Log($"{id} 重新上線");
    }

    void Update()
    {
        if (edgeLink.Raw == lastRaw) return;
        lastRaw = edgeLink.Raw;

        string id    = edgeLink.Get("id");
        string temp  = edgeLink.Get("temp");
        string humid = edgeLink.Get("humid");

        Debug.Log($"[{id}] temp={temp} humid={humid}");
    }
}
