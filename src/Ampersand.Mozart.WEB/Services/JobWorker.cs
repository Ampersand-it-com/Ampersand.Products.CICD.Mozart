using System.Diagnostics;
using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Drains the job queue, honouring the configured parallelism limit.
/// </summary>
public sealed class JobWorker(JobManager jobs, ProcessRunner runner, ProjectStore store, JobLogStore logs, MozartErrorLog errorLog, ILogger<JobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var slots = new SemaphoreSlim(Math.Max(1, jobs.Options.MaxParallelJobs));

        try
        {
            await foreach (var job in jobs.Reader.ReadAllAsync(stoppingToken))
            {
                if (job.Status == JobStatus.Canceled)
                {
                    continue;
                }

                await slots.WaitAsync(stoppingToken);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteJobAsync(job, stoppingToken);
                    }
                    finally
                    {
                        slots.Release();
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — expected.
        }
    }

    private async Task ExecuteJobAsync(Job job, CancellationToken stoppingToken)
    {
        using var cts = jobs.RegisterRunning(job, stoppingToken);
        job.Status = JobStatus.Running;
        job.StartedUtc = DateTimeOffset.UtcNow;
        logs.Start(job);
        jobs.NotifyChanged();

        var lastNotify = DateTimeOffset.UtcNow;

        try
        {
            var exitCode = await runner.StreamAsync(
                job.FileName,
                job.Arguments,
                job.WorkingDirectory,
                (severity, line) =>
                {
                    job.Append(severity, line);
                    logs.Append(job, severity, line);
                    if (severity == LogSeverity.Error)
                    {
                        job.ErrorCount++;
                    }
                    else if (severity == LogSeverity.Warning)
                    {
                        job.WarningCount++;
                    }

                    CaptureSummary(job, line);

                    // Coalesce UI notifications so a chatty build does not flood the circuit.
                    if (DateTimeOffset.UtcNow - lastNotify > TimeSpan.FromMilliseconds(400))
                    {
                        lastNotify = DateTimeOffset.UtcNow;
                        jobs.NotifyChanged();
                    }
                },
                cts.Token);

            job.ExitCode = exitCode;
            job.Status = exitCode == 0 ? JobStatus.Succeeded : JobStatus.Failed;
            job.Append(LogSeverity.System, $"Exit code: {exitCode}");
            logs.Append(job, LogSeverity.System, $"Exit code: {exitCode}");

            if (job.Status == JobStatus.Succeeded && job.RequiredLogHints.Length > 0)
            {
                var log = job.PlainLog();
                if (!job.RequiredLogHints.Any(hint => log.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                {
                    job.Status = JobStatus.Failed;
                    job.Summary = "Build finished, but Web Deploy did not run. Check the profile.";
                    job.Append(LogSeverity.Error,
                        "dotnet exited 0, but the log has no Web Deploy output. " +
                        "Mozart published locally (or skipped MSDeploy). Pick the IIS Web Deploy profile, not an empty .pubxml.");
                    logs.Append(job, LogSeverity.Error, job.Summary!);
                }
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Canceled;
            job.Append(LogSeverity.System, "Job canceled.");
            logs.Append(job, LogSeverity.System, "Job canceled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} crashed", job.Id);
            job.Status = JobStatus.Failed;
            job.Append(LogSeverity.Error, ex.Message);
            logs.Append(job, LogSeverity.Error, ex.Message);
        }
        finally
        {
            job.FinishedUtc = DateTimeOffset.UtcNow;
            jobs.UnregisterRunning(job);
            if (job.Kind == JobKind.Publish)
            {
                await RememberDeployResultAsync(job);
                if (job.Status == JobStatus.Succeeded)
                {
                    OpenBrowser(job);
                }
            }

            try
            {
                logs.Complete(job);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not finish job log file for {Job}", job.Id);
            }

            if (job.Status == JobStatus.Failed)
            {
                errorLog.WriteJobFailure(job);
            }

            jobs.NotifyChanged();
        }
    }

    private static void CaptureSummary(Job job, string line)
    {
        var trimmed = line.Trim();

        if (job.Kind == JobKind.Test &&
            (trimmed.StartsWith("Passed!", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("Failed!", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("Test summary:", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("total:", StringComparison.OrdinalIgnoreCase)))
        {
            job.Summary = trimmed;
            return;
        }

        if (job.Kind == JobKind.Publish && trimmed.Contains("published to", StringComparison.OrdinalIgnoreCase))
        {
            job.Summary = trimmed;
        }
    }

    private void OpenBrowser(Job job)
    {
        var url = job.LaunchUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            job.Append(LogSeverity.System, "Deploy ok, no site URL to open (set SiteUrlToLaunchAfterPublish or Health URL).");
            logs.Append(job, LogSeverity.System, "Deploy ok, no site URL to open.");
            return;
        }

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var opened = $"{url.TrimEnd('/')}{separator}t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = opened,
                UseShellExecute = true,
            });
            job.Append(LogSeverity.System, $"Opened browser: {opened}");
            logs.Append(job, LogSeverity.System, $"Opened browser: {opened}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open {Url}", opened);
            job.Append(LogSeverity.Warning, $"Could not open browser: {ex.Message}");
            logs.Append(job, LogSeverity.Warning, $"Could not open browser: {ex.Message}");
        }
    }

    private async Task RememberDeployResultAsync(Job job)
    {
        try
        {
            var project = await store.GetAsync(job.ProjectId);
            if (project is null)
            {
                return;
            }

            project.LastDeployError = job.Status == JobStatus.Succeeded ? null : job.Diagnose();
            await store.SaveAsync(project);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist deploy result for {Job}", job.Id);
        }
    }
}
