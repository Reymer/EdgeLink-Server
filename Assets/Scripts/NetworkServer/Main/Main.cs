using DevKit.Console;
using UnityEngine;

public class Main : MonoBehaviour
{
    [SerializeField] private NetworkPortTableUIManager networkPortTableUIManager;
    [SerializeField] private ConsoleUI consoleUI;
    [SerializeField] private MonitorConsole monitorConsole;

    private void Start()
    {
        Init();
    }

    private void Init()
    {
        NetworkPortManager.Instance.Init(consoleUI, monitorConsole);
        networkPortTableUIManager.Init();
    }

    private void OnApplicationQuit()
    {
        NetworkPortManager.Instance.UnInit();
        networkPortTableUIManager.UnInit();
    }
}
