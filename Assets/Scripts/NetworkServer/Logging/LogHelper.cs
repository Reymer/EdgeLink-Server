using Cysharp.Threading.Tasks;
using DevKit.Console;
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

/// <summary>
/// 主線程日誌工具類
/// </summary>
public static class LogHelper
{
    private static MonitorConsole monitor;
    private static ConsoleUI consoleUI;

    private static volatile bool _isShuttingDown = false;

    // 批次緩衝：每 frame 只 flush 一次，避免每條 log 各觸發一次 Canvas rebuild
    private static readonly ConcurrentQueue<string> _pendingMonitorLogs = new();
    private static int _monitorFlushScheduled; // 0=idle, 1=scheduled

    // Web API 用環形緩衝區，保留最近 200 條 console log
    private const int WEB_LOG_BUFFER = 200;
    private static readonly string[] _webLogBuffer = new string[WEB_LOG_BUFFER];
    private static int _webLogHead = 0;
    private static int _webLogTotal = 0;
    private static readonly object _webLogLock = new();

    // Web API 用環形緩衝區，保留最近 500 條 monitor log
    private const int WEB_MONITOR_BUFFER = 500;
    private static readonly string[] _webMonitorBuffer = new string[WEB_MONITOR_BUFFER];
    private static int _webMonitorHead = 0;
    private static int _webMonitorTotal = 0;
    private static readonly object _webMonitorLock = new();

    /// <summary>
    /// 初始化 LogHelper
    /// </summary>
    public static void Init(MonitorConsole monitorConsole, ConsoleUI consoleUIInstance)
    {
        _isShuttingDown = false;
        monitor = monitorConsole;
        consoleUI = consoleUIInstance;
    }

    /// <summary>
    /// 關閉時呼叫：停止接受新 log，清空未處理的佇列
    /// </summary>
    public static void Shutdown()
    {
        _isShuttingDown = true;
        while (_pendingMonitorLogs.TryDequeue(out _)) { }
    }

    /// <summary>
    /// 切換監控目標時呼叫：清空尚未刷出的 monitor log 佇列
    /// </summary>
    public static void ClearPendingMonitorLogs()
    {
        while (_pendingMonitorLogs.TryDequeue(out _)) { }
    }

    /// <summary>
    /// 輸出日誌到 MonitorConsole（批次，每 frame 合併為一次 flush）
    /// </summary>
    public static void LogToMonitor(string message)
    {
        if (_isShuttingDown) return;
        string stamped = FormatLogMessage(message, false);

        lock (_webMonitorLock)
        {
            _webMonitorBuffer[_webMonitorHead] = stamped;
            _webMonitorHead = (_webMonitorHead + 1) % WEB_MONITOR_BUFFER;
            _webMonitorTotal++;
        }

        MonitorSseHandler.Publish(stamped);

        _pendingMonitorLogs.Enqueue(stamped);

        if (Interlocked.CompareExchange(ref _monitorFlushScheduled, 1, 0) == 0)
        {
            UniTask.Post(() =>
            {
                Interlocked.Exchange(ref _monitorFlushScheduled, 0);
                while (_pendingMonitorLogs.TryDequeue(out var msg))
                    monitor?.AddLog(msg);
            }, PlayerLoopTiming.Update);
        }
    }

    /// <summary>
    /// 輸出日誌到 ConsoleUI（如果有），自帶格式化與截斷
    /// </summary>
    public static void LogToConsole(string message, bool isError = false)
    {
        if (_isShuttingDown || string.IsNullOrEmpty(message))
            return;

        if (message.Length > 1000)
        {
            message = message[..1000] + "... (truncated)";
        }

        string formattedMessage = FormatLogMessage(message, isError);

        lock (_webLogLock)
        {
            _webLogBuffer[_webLogHead] = formattedMessage;
            _webLogHead = (_webLogHead + 1) % WEB_LOG_BUFFER;
            _webLogTotal++;
        }

        UniTask.Post(() =>
        {
            if (isError)
                Debug.LogError(formattedMessage);
            else
                Debug.Log(formattedMessage);

            consoleUI?.AddLog(formattedMessage);
        }, PlayerLoopTiming.Update);
    }

    /// <summary>
    /// 供 Web API 讀取：回傳 cursor 之後的新 log 與當前 total。
    /// cursor = 上次回傳的 total；total 單調遞增，不受緩衝區大小限制。
    /// </summary>
    public static (int total, string[] logs) GetMonitorLogsSince(int cursor)
    {
        lock (_webLogLock)
        {
            int total = _webLogTotal;
            if (cursor >= total)
                return (total, Array.Empty<string>());

            // 最多只能往回看 WEB_LOG_BUFFER 條
            int available = Math.Min(total - cursor, WEB_LOG_BUFFER);
            int startSlot = (_webLogHead - available + WEB_LOG_BUFFER * 2) % WEB_LOG_BUFFER;
            var result = new string[available];
            for (int i = 0; i < available; i++)
                result[i] = _webLogBuffer[(startSlot + i) % WEB_LOG_BUFFER] ?? "";
            return (total, result);
        }
    }

    /// <summary>
    /// 供 Web API 讀取 monitor log，cursor 之後的新條目。
    /// </summary>
    public static (int total, string[] logs) GetWebMonitorLogsSince(int cursor)
    {
        lock (_webMonitorLock)
        {
            int total = _webMonitorTotal;
            if (cursor >= total)
                return (total, Array.Empty<string>());

            int available = Math.Min(total - cursor, WEB_MONITOR_BUFFER);
            int startSlot = (_webMonitorHead - available + WEB_MONITOR_BUFFER * 2) % WEB_MONITOR_BUFFER;
            var result = new string[available];
            for (int i = 0; i < available; i++)
                result[i] = _webMonitorBuffer[(startSlot + i) % WEB_MONITOR_BUFFER] ?? "";
            return (total, result);
        }
    }

    /// <summary>
    /// 統一 log 前綴：[Protocol | Name #shortId]
    /// </summary>
    public static string Tag(string protocol, string name) => $"[{protocol} | {name}]";

    public static string Tag(string protocol, PortData portData)
    {
        string shortId = !string.IsNullOrEmpty(portData?.Id) ? " #" + portData.Id[..8] : "";
        return $"[{protocol} | {portData?.ProtocolName}{shortId}]";
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
