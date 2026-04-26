using UnityEngine;

public class MonitorConsole : MonoBehaviour
{
    [SerializeField] private GameObject logUIPrefab;
    [SerializeField] private Transform logRoot;
    private const int MAX_LOG = 200;

    private LogUI[] pool;
    private int next; // 下一個要覆寫的 slot

    private void Awake()
    {
        pool = new LogUI[MAX_LOG];
        for (int i = 0; i < MAX_LOG; i++)
        {
            var go = Instantiate(logUIPrefab, logRoot);
            go.SetActive(false);
            pool[i] = go.GetComponent<LogUI>();
        }
    }

    public void AddLog(string log)
    {
        var slot = pool[next];
        slot.Log(log);
        slot.gameObject.SetActive(true);
        slot.transform.SetAsLastSibling(); // 移到最底（最新）
        next = (next + 1) % MAX_LOG;
    }

    public void RemoveAll()
    {
        foreach (var ui in pool)
            if (ui != null) ui.gameObject.SetActive(false);
        next = 0;
    }
}
