using DevKit;
using System.Collections.Generic;
using UnityEngine;

public class MonitorConsole : MonoBehaviour
{
    [SerializeField]
    private GameObject logUIPrefab;
    [SerializeField]
    private Transform logRoot;
    private const int maxLogCount = 200;

    private List<GameObject> objects = new List<GameObject>();

    public void AddLog(string log)
    {
        if (objects.Count >= maxLogCount)
        {
            RemoveOldestLog();
        }

        GameObject logObj = Instantiate(logUIPrefab, logRoot);
        if (logObj.TryGetComponent<LogUI>(out var logUI))
        {
            logUI.Log(log);
            objects.Add(logObj);
        }
    }

    private void RemoveOldestLog()
    {
        if (objects.Count > 0)
        {
            GameObject oldestLog = objects[0];
            objects.RemoveAt(0);
            Destroy(oldestLog);
        }
    }

    public void RemoveAll()
    {
        foreach (var logObj in objects)
        {
            Destroy(logObj);
        }
        objects.Clear();
    }
}
