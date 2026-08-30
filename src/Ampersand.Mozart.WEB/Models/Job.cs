using System.Collections.Concurrent;

namespace Ampersand.Mozart.WEB.Models;

public enum JobKind
{
    Restore,
    Build,
    Test,
    Publish,
    Clean,
    GitPull,
    GitCommitPush,
    Custom,
    Run,
}

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

public enum LogSeverity
{
    Info,
    Warning,
    Error,
    System,
}

public sealed record JobLogLine(DateTimeOffset Timestamp, LogSeverity Severity, string Text);

/// <summary>
/// One execution of a dotnet/git command started from the panel.
/// </summary>
public sealed class Job
{
    private readonly ConcurrentQueue<JobLogLine> _log = new();

    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public required string ProjectId { get; init; }

    public required string ProjectName { get; init; }

    public required JobKind Kind { get; init; }

    public required string Title { get; init; }

    public required string FileName { get; init; }

    public required string Arguments { get; init; }

    public required string WorkingDirectory { get; init; }

    /// <summary>Arguments with secrets masked, safe to render in the UI.</summary>
    public string DisplayArguments { get; init; } = string.Empty;

    public JobStatus Status { get; set; } = JobStatus.Queued;

    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedUtc { get; set; }

    public DateTimeOffset? FinishedUtc { get; set; }

    public int? ExitCode { get; set; }

    public int WarningCount { get; set; }

    public int ErrorCount { get; set; }

    /// <summary>Short result line shown in lists, e.g. test totals.</summary>
    public string? Summary { get; set; }

    public string? LogFilePath { get; set; }

    /// <summary>
    /// After exit 0, at least one of these fragments must appear in the log.
    /// Used to catch "build ok, Web Deploy never ran".
    /// </summary>
    public string[] RequiredLogHints { get; init; } = [];

    /// <summary>Opened in the default browser after a successful deploy.</summary>
    public string? LaunchUrl { get; init; }

    public TimeSpan? Duration => StartedUtc is null
        ? null
        : (FinishedUtc ?? DateTimeOffset.UtcNow) - StartedUtc.Value;

    public bool IsActive => Status is JobStatus.Queued or JobStatus.Running;

    public int LogCount => _log.Count;

    public void Append(LogSeverity severity, string text)
    {
        _log.Enqueue(new JobLogLine(DateTimeOffset.UtcNow, severity, text));
        while (_log.Count > 4000 && _log.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<JobLogLine> Snapshot() => [.. _log];

    public string PlainLog() => string.Join(Environment.NewLine, _log.Select(l => l.Text));

    /// <summary>Human-readable why this job failed (or the last useful line).</summary>
    public string? Diagnose()
    {
        if (!string.IsNullOrWhiteSpace(Summary) && Status != JobStatus.Succeeded)
        {
            return Summary;
        }

        var errors = Snapshot()
            .Where(l => l.Severity == LogSeverity.Error)
            .Select(l => l.Text.Trim())
            .Where(t => t.Length > 0)
            .Distinct()
            .TakeLast(3)
            .ToList();

        if (errors.Count > 0)
        {
            return string.Join(" · ", errors);
        }

        if (Status == JobStatus.Failed)
        {
            return ExitCode is null
                ? "Failed with no error in the log. Open the job."
                : $"Exit code {ExitCode}. Open the log.";
        }

        return Summary;
    }
}
