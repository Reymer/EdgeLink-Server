using EdgeLink.Infrastructure;

AppLogger.Log("[EdgeLink] Starting...");

// 未捕捉例外處理
AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    AppLogger.Error($"[Critical] Unhandled exception: {args.ExceptionObject}");

TaskScheduler.UnobservedTaskException += (_, args) =>
{
    AppLogger.Error($"[Critical] Unobserved task exception: {args.Exception.Message}");
    args.SetObserved();
};

// TODO: 初始化 NetworkPortManager、HttpApiServer 等核心元件
// (逐步從 Unity Main.cs 搬移)

AppLogger.Log("[EdgeLink] Press Ctrl+C to stop.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

AppLogger.Log("[EdgeLink] Shutting down...");
