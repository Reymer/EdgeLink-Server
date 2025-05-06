using DevKit.Console;
using UnityEngine;

public class Main : MonoBehaviour
{
    [SerializeField] private NetworkPortTableUIManager networkPortTableUIManager;
    [SerializeField] private ConsoleUI consoleUI;
    [SerializeField] private MonitorConsole monitorConsole;
    private readonly DeviceLockProcessor deviceLockProcessor = new();

    private void Start()
    {
        Init();
    }

    /// <summary>
    /// 初始化
    /// </summary>
    private void Init()
    {
        //deviceLockProcessor.Init();
        NetworkPortManager.Instance.Init(consoleUI, monitorConsole);
        networkPortTableUIManager.Init();
    }

    private void Update()
    {
        //deviceLockProcessor.Update();
    }

    /// <summary>
    /// 退出應用程式時調用
    /// </summary>
    private void OnApplicationQuit()
    {
        //deviceLockProcessor.UnInit();   
        NetworkPortManager.Instance.UnInit();
        networkPortTableUIManager.UnInit();
    }
}
