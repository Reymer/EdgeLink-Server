using DevKit.Console;
using UnityEngine;

public class Main : MonoBehaviour
{
    [SerializeField] private NetworkPortTableUIManager networkPortTableUIManager;
    [SerializeField] private ConsoleUI consoleUI;
    [SerializeField] private MonitorConsole monitorConsole;

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
        // 捕獲未處理的域異常
        System.AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = args.ExceptionObject as System.Exception;
            Debug.LogError($"[Critical] 未處理的域異常: {exception?.Message}\n{exception?.StackTrace}");
            LogHelper.LogToConsole($"[Critical] 未處理的域異常: {exception?.Message}", isError: true);
        };

        // 捕獲未觀察的 Task 異常（最關鍵！）
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Debug.LogError($"[Critical] 未觀察的 Task 異常: {args.Exception.Message}\n{args.Exception.StackTrace}");
            LogHelper.LogToConsole($"[Critical] 未觀察的 Task 異常: {args.Exception.Message}", isError: true);

            // 標記為已處理，防止應用崩潰
            args.SetObserved();
        };

        Debug.Log("[Main] 全局異常處理器已設置");
    }

    /// <summary>
    /// 初始化
    /// </summary>
    private void Init()
    {
        NetworkPortManager.Instance.Init(consoleUI, monitorConsole);
        networkPortTableUIManager.Init();
    }

    /// <summary>
    /// 退出應用程式時調用
    /// </summary>
    private void OnApplicationQuit()
    {
        Debug.Log("[Main] 開始關閉應用程式...");

        try
        {
            // 使用超時機制防止卡住
            var shutdownTask = NetworkPortManager.Instance.UnInit();

            // 等待最多 2 秒，超時則強制繼續
            if (!shutdownTask.Wait(2000))
            {
                Debug.LogWarning("[Main] 關閉網路連接超時（2秒），強制退出");
            }
            else
            {
                Debug.Log("[Main] 網路連接已正常關閉");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Main] 關閉網路連接時發生錯誤: {ex.Message}");
        }

        try
        {
            networkPortTableUIManager.UnInit();
            Debug.Log("[Main] UI 管理器已關閉");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Main] 關閉 UI 管理器時發生錯誤: {ex.Message}");
        }

        Debug.Log("[Main] 應用程式關閉完成");
    }
}
