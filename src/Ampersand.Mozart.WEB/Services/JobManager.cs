using System.Collections.Concurrent;
using System.Threading.Channels;
using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Queue and history of commands executed by the panel.
/// </summary>
public sealed class JobManager(IOptions<MozartOptions> options, ILogger<JobManager> logger)
{
    private readonly Channel<Job> _queue = Channel.CreateUnbounded<Job>();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    private readonly List<Job> _history = [];
    private readonly Lock _historyLock = new();
    private readonly MozartOptions _options = options.Value;

    public event Action? Changed;

    public ChannelReader<Job> Reader => _queue.Reader;

    public MozartOptions Options => _options;

    public Job Enqueue(Job job)
    {
        lock (_historyLock)
        {
            _history.Insert(0, job);
            var limit = Math.Max(20, _options.JobHistoryLimit);
            if (_history.Count > limit)
            {
                var stale = _history.Skip(limit).Where(j => !j.IsActive).ToList();
                foreach (var item in stale)
                {
                    _history.Remove(item);
                }
            }
        }

        job.Append(LogSeverity.System, $"$ {job.FileName} {job.DisplayArguments}");
        job.Append(LogSeverity.System, $"cwd: {job.WorkingDirectory}");
        _queue.Writer.TryWrite(job);
        logger.LogInformation("Job {Id} queued: {Title}", job.Id, job.Title);
        NotifyChanged();
        return job;
    }

    public IReadOnlyList<Job> GetJobs(string? projectId = null, int take = 100)
    {
        lock (_historyLock)
        {
            var query = _history.AsEnumerable();
            if (projectId is not null)
            {
                query = query.Where(j => j.ProjectId == projectId);
            }

            return [.. query.Take(take)];
        }
    }

    public Job? GetJob(string id)
    {
        lock (_historyLock)
        {
            return _history.FirstOrDefault(j => j.Id == id);
        }
    }

    public IReadOnlyList<Job> GetActiveJobs()
    {
        lock (_historyLock)
        {
            return [.. _history.Where(j => j.IsActive)];
        }
    }

    public Job? GetLastFinished(string projectId, JobKind kind)
    {
        lock (_historyLock)
        {
            return _history.FirstOrDefault(j => j.ProjectId == projectId && j.Kind == kind && !j.IsActive);
        }
    }

    public Job? GetRunningRunJob(string projectId)
    {
        lock (_historyLock)
        {
            return _history.FirstOrDefault(
                j => j.ProjectId == projectId && j.Kind == JobKind.Run && j.Status == JobStatus.Running);
        }
    }

    public bool StopRun(string projectId)
    {
        var job = GetRunningRunJob(projectId);
        return job is not null && Cancel(job.Id);
    }

    public bool Cancel(string jobId)
    {
        if (_running.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        var job = GetJob(jobId);
        if (job is { Status: JobStatus.Queued })
        {
            job.Status = JobStatus.Canceled;
            job.FinishedUtc = DateTimeOffset.UtcNow;
            job.Append(LogSeverity.System, "Canceled before start.");
            NotifyChanged();
            return true;
        }

        return false;
    }

    public void ClearFinished()
    {
        lock (_historyLock)
        {
            _history.RemoveAll(j => !j.IsActive);
        }

        NotifyChanged();
    }

    internal CancellationTokenSource RegisterRunning(Job job, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (job.Kind != JobKind.Run)
        {
            cts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.JobTimeoutMinutes)));
        }
        else if (_options.RunTimeoutMinutes > 0)
        {
            cts.CancelAfter(TimeSpan.FromMinutes(_options.RunTimeoutMinutes));
        }

        _running[job.Id] = cts;
        return cts;
    }

    internal void UnregisterRunning(Job job)
    {
        if (_running.TryRemove(job.Id, out var cts))
        {
            cts.Dispose();
        }
    }

    internal void NotifyChanged() => Changed?.Invoke();
}
