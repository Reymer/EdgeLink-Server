using DevKit.Console;
using System;
using UnityEngine;

/// <summary>
/// 主線程日誌工具類
/// </summary>
public static class LogHelper
{
    private static MonitorConsole monitor;
    private static ConsoleUI consoleUI;

    /// <summary>
    /// 初始化 LogHelper
    /// </summary>
    public static void Init(MonitorConsole monitorConsole, ConsoleUI consoleUIInstance)
    {
        monitor = monitorConsole;
        consoleUI = consoleUIInstance;
    }

    /// <summary>
    /// 輸出日誌到 MonitorConsole（如果有）
    /// </summary>
    public static void LogToMonitor(string message)
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (monitor != null)
            {
                monitor.AddLog(message);
            }
            else
            {
                Debug.LogWarning("MonitorConsole is null, cannot add log.");
            }
        });
    }

    /// <summary>
    /// 輸出日誌到 ConsoleUI（如果有），自帶格式化與截斷
    /// </summary>
    public static void LogToConsole(string message, bool isError = false)
    {
        if (string.IsNullOrEmpty(message))
            return;

        if (message.Length > 1000)
        {
            message = message[..1000] + "... (truncated)";
        }

        string formattedMessage = FormatLogMessage(message, isError);

        try
        {
            var dispatcher = UnityMainThreadDispatcher.Instance();

            if (dispatcher != null)
            {
                dispatcher.Enqueue(() =>
                {
                    if (isError)
                    {
                        Debug.LogError(formattedMessage);
                    }
                    else
                    {
                        Debug.Log(formattedMessage);
                    }

                    if (consoleUI != null)
                    {
                        consoleUI.AddLog(formattedMessage);
                    }
                    else
                    {
                        Debug.LogWarning("ConsoleUI is null, cannot add log.");
                    }
                });
            }
            else
            {
                if (isError)
                    Debug.LogError(formattedMessage);
                else
                    Debug.Log(formattedMessage);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LogToConsole] 捕捉到例外: {ex.Message}");
            Debug.LogError($"欲記錄之訊息: {formattedMessage}");
        }
    }



    /// <summary>
    /// 格式化日誌訊息
    /// </summary>
    private static string FormatLogMessage(string message, bool isError)
    {
        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string errorLabel = isError ? "[Error]" : "[Info]";
        return $"[{timeStamp}] {errorLabel} {message}";
    }
}
