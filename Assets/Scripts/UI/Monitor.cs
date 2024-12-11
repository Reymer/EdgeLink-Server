using DevKit.Tool;
using UnityEngine;

public class Monitor : MonoBehaviour
{
    private UICollector uiCollector;

    private void Start()
    {
        Init();
        SetStatus(UIKey.Monitor_Monitor, false);
    }

    private void Init()
    {
        uiCollector = GetComponent<UICollector>();
        uiCollector.BindOnCheck(UIKey.Monitor_Close, () => SetStatus(UIKey.Monitor_Monitor, false));
    }

    public void SetStatus(string key, bool status)
    {
        uiCollector.SetActive(key, status);
    }
}
