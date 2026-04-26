using TMPro;
using UnityEngine;

/// <summary>
/// 替換 DevKit LogUI，代表 MonitorConsole pool 中的一筆 log 項目。
/// </summary>
public class LogUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private void Awake()
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>();
    }

    public void Log(string message)
    {
        if (label != null) label.text = message;
    }
}
