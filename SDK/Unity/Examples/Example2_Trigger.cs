using UnityEngine;

/// <summary>
/// 範例二：根據感測器數值觸發場景反應
/// 用法：掛在任意 GameObject，把 EdgeLink GameObject 拖入 edgeLinkObject 欄位
/// </summary>
public class Example2_Trigger : MonoBehaviour
{
    public GameObject edgeLinkObject;
    public GameObject warningPanel;   // 超過閾值時顯示的警告面板
    public Light      sceneLight;     // 根據溫度改變燈光顏色
    public float      tempThreshold = 30f;

    EdgeLinkManager edgeLink;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        if (edgeLink.Raw == null) return;

        string tempStr  = edgeLink.Get("temp");
        string statusStr = edgeLink.Get("status");

        // 溫度超過閾值 → 顯示警告
        if (float.TryParse(tempStr, out float temp))
        {
            bool isOverheat = temp >= tempThreshold;
            warningPanel.SetActive(isOverheat);
            sceneLight.color = isOverheat ? Color.red : Color.white;
        }

        // 狀態異常 → 閃爍燈光
        if (statusStr == "WARN")
            sceneLight.intensity = Mathf.PingPong(Time.time * 4f, 1f);
        else
            sceneLight.intensity = 1f;
    }
}
