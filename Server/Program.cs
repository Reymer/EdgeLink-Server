using EdgeLink.Infrastructure;
using EdgeLink.NetworkServer.Connector;
using EdgeLink.NetworkServer.Logging;
using EdgeLink.NetworkServer.Services;
using EdgeLink.WebApi;

AppLogger.Log("[EdgeLink] Starting...");

AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    AppLogger.Error($"[Critical] Unhandled exception: {args.ExceptionObject}");

TaskScheduler.UnobservedTaskException += (_, args) =>
{
    AppLogger.Error($"[Critical] Unobserved task exception: {args.Exception.Message}");
    args.SetObserved();
};

// ── Init core ────────────────────────────────────────────────────────────────

var core = new NetworkConnectorCore();
core.Init(MonitorSseHandler.Publish);

PortManager.Initialize(core);
PortManager.Instance.LoadAndStart();

var httpServer = new HttpApiServer();
httpServer.Start(port: 8080, webUiPath: AppPaths.WebUiIndex);

AppLogger.Log("[EdgeLink] Running — press Ctrl+C to stop.");

// ── Wait for shutdown ────────────────────────────────────────────────────────

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

// ── Shutdown ─────────────────────────────────────────────────────────────────

AppLogger.Log("[EdgeLink] Shutting down...");
httpServer.Stop();
await PortManager.Instance.ShutdownAsync();
LogHelper.Shutdown();
AppLogger.Log("[EdgeLink] Stopped.");
