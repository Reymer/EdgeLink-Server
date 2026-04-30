using EdgeLink;
using EdgeLink.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    AppLogger.Error($"[Critical] Unhandled exception: {e.ExceptionObject}");

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    AppLogger.Error($"[Critical] Unobserved task: {e.Exception.Message}");
    e.SetObserved();
};

await Host.CreateDefaultBuilder(args)
    .ConfigureLogging(b => b.ClearProviders())
    .ConfigureServices((_, services) =>
    {
        services.AddSingleton(AppConfig.FromArgs(args));
        services.AddHostedService<EdgeLinkService>();
    })
    .Build()
    .RunAsync();
