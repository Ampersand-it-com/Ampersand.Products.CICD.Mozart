using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Periodically refreshes health/ping data so the dashboard is warm on open.
/// </summary>
public sealed class HealthMonitor(
    ProjectStore store,
    HealthProbeService probe,
    IOptions<MozartOptions> options,
    ILogger<HealthMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.HealthPollSeconds;
        if (interval <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, interval)));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var project in await store.GetAllAsync())
                    {
                        if (!project.IsEnabled || (project.HealthUrl is null && project.PingHost is null))
                        {
                            continue;
                        }

                        await probe.ProbeAsync(project, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Health polling iteration failed");
                }

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down while waiting for the next tick — expected.
        }
    }
}
