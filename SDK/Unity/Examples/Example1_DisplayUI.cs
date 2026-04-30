using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 範例一：把感測器數值即時顯示在 UI 上
/// 用法：掛在任意 GameObject，把 EdgeLink GameObject 拖入 edgeLinkObject 欄位
/// </summary>
public class Example1_DisplayUI : MonoBehaviour
{
    public GameObject edgeLinkObject;
    public Text       tempText;
    public Text       humidText;
    public Text       statusText;

    EdgeLinkManager edgeLink;

    void Start()
    {
        edgeLink = edgeLinkObject.GetComponent<EdgeLinkManager>();
    }

    void Update()
    {
        if (edgeLink.Raw == null) return;

        tempText.text   = edgeLink.Get("temp")   ?? "--";
        humidText.text  = edgeLink.Get("humid")  ?? "--";
        statusText.text = edgeLink.Get("status") ?? "--";
    }
}
