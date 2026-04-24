using DevKit.Console;
using System.IO;
using UnityEngine;

public class Main : MonoBehaviour
{
    [SerializeField] private NetworkPortTableUIManager networkPortTableUIManager;
    [SerializeField] private ConsoleUI consoleUI;
    [SerializeField] private MonitorConsole monitorConsole;

    private HttpApiServer _httpApiServer;

    private void Start()
    {
        SetupGlobalExceptionHandlers();
        Init();
    }

    /// <summary>
    /// 設置全局異常處理器
    /// </summary>
    private void SetupGlobalExceptionHandlers()
    {
        System.AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = args.ExceptionObject as System.Exception;
            Debug.LogError($"[Critical] Unhandled domain exception: {exception?.Message}\n{exception?.StackTrace}");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Debug.LogError($"[Critical] Unobserved task exception: {args.Exception.Message}\n{args.Exception.StackTrace}");
            args.SetObserved();
        };
    }

    /// <summary>
    /// 初始化
    /// </summary>
    private void Init()
    {
        NetworkPortManager.Instance.Init(consoleUI, monitorConsole);
        networkPortTableUIManager.Init();

        _httpApiServer = new HttpApiServer();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string webUiPath = Path.Combine(projectRoot, "IOT-Server", "WebUI", "index.html");
        _httpApiServer.Start(port: 8181, webUiPath: webUiPath);
    }

    /// <summary>
    /// 退出應用程式時調用
    /// </summary>
    private void OnApplicationQuit()
    {
        LogHelper.Shutdown();

        try
        {
            _httpApiServer?.Stop();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Main] Error stopping HTTP API: {ex.Message}");
        }

        try
        {
            var shutdownTask = NetworkPortManager.Instance.UnInit();
            if (!shutdownTask.Wait(2000))
                Debug.LogWarning("[Main] Network shutdown timed out (2s), forcing exit");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Main] Error shutting down network: {ex.Message}");
        }

        try
        {
            networkPortTableUIManager.UnInit();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Main] Error shutting down UI manager: {ex.Message}");
        }
    }
}
