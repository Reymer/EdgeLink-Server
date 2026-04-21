using DevKit;
using UnityEngine;

public class MonitorConsole : MonoBehaviour
{
    [SerializeField] private GameObject logUIPrefab;
    [SerializeField] private Transform logRoot;
    private const int maxLogCount = 200;

    public void AddLog(string log)
    {
        if (logRoot.childCount >= maxLogCount)
            Destroy(logRoot.GetChild(0).gameObject);

        var logObj = Instantiate(logUIPrefab, logRoot);
        if (logObj.TryGetComponent<LogUI>(out var logUI))
            logUI.Log(log);
    }

    public void RemoveAll()
    {
        for (int i = logRoot.childCount - 1; i >= 0; i--)
            Destroy(logRoot.GetChild(i).gameObject);
    }
}
