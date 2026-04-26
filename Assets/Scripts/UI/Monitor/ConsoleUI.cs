using TMPro;
using UnityEngine;

/// <summary>
/// 替換 DevKit ConsoleUI，在 ScrollView 內動態生成文字行顯示 log。
/// </summary>
public class ConsoleUI : MonoBehaviour
{
    [SerializeField] private GameObject logLinePrefab;
    [SerializeField] private Transform  logRoot;

    public void AddLog(string message)
    {
        if (logLinePrefab == null || logRoot == null)
        {
            Debug.Log($"[ConsoleUI] {message}");
            return;
        }

        var go  = Instantiate(logLinePrefab, logRoot);
        var tmp = go.GetComponent<TMP_Text>();
        if (tmp != null) tmp.text = message;
        go.transform.SetAsLastSibling();
    }
}
