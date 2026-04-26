using UnityEngine;

public class ConsoleUI : MonoBehaviour
{
    [SerializeField] private GameObject logUIPrefab;
    [SerializeField] private Transform  logRoot;

    public void AddLog(string log)
    {
        if (logUIPrefab == null || logRoot == null) return;
        var go    = Instantiate(logUIPrefab, logRoot);
        var logUI = go.GetComponent<LogUI>();
        logUI?.Log(log);
    }
}
