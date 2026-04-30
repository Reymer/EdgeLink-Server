using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 範例三：記錄歷史資料，顯示最近 N 筆
/// 用法：掛在任意 GameObject，把 EdgeLink GameObject 拖入 edgeLinkObject 欄位
/// </summary>
public class Example3_DataLog : MonoBehaviour
{
    public GameObject edgeLinkObject;
    public Text       logText;
    public int        maxLines = 10;

    EdgeLinkManager   edgeLink;
    string            lastRaw;
    Queue<string>     log = new();

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        if (edgeLink.Raw == null || edgeLink.Raw == lastRaw) return;

        lastRaw = edgeLink.Raw;

        string line = $"[{System.DateTime.Now:HH:mm:ss}] {edgeLink.Raw}";
        log.Enqueue(line);

        if (log.Count > maxLines)
            log.Dequeue();

        logText.text = string.Join("\n", log);
    }
}
