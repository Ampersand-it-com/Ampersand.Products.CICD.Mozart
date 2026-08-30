using System.Text;
using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Append-only error log on disk — share <see cref="FilePath"/> with the agent for triage.
/// </summary>
public sealed class MozartErrorLog
{
    private const long MaxBytesBeforeRotate = 5 * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly string _filePath;

    public MozartErrorLog(IOptions<MozartOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.DataDirectory;
        var root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);

        var logsDir = Path.Combine(root, "logs");
        Directory.CreateDirectory(logsDir);
        _filePath = Path.Combine(logsDir, "mozart-errors.log");
    }

    public string FilePath => _filePath;

    public void Write(string source, string message, Exception? exception = null, string? details = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{source}] {message}");
        if (!string.IsNullOrWhiteSpace(details))
        {
            sb.AppendLine(details.TrimEnd());
        }

        if (exception is not null)
        {
            sb.AppendLine(exception.ToString());
        }

        sb.AppendLine("---");

        lock (_lock)
        {
            RotateIfNeeded();
            File.AppendAllText(_filePath, sb.ToString(), Encoding.UTF8);
        }
    }

    public void WriteJobFailure(Job job)
    {
        var details = new StringBuilder();
        details.AppendLine($"JobId: {job.Id}");
        details.AppendLine($"Project: {job.ProjectName}");
        details.AppendLine($"Kind: {job.Kind}");
        details.AppendLine($"Title: {job.Title}");
        details.AppendLine($"Status: {job.Status}");
        details.AppendLine($"ExitCode: {job.ExitCode}");
        details.AppendLine($"Reason: {job.Diagnose()}");
        details.AppendLine($"Command: {job.FileName} {job.DisplayArguments}");
        details.AppendLine($"Cwd: {job.WorkingDirectory}");
        if (!string.IsNullOrWhiteSpace(job.LogFilePath))
        {
            details.AppendLine($"JobLog: {job.LogFilePath}");
        }

        var errors = job.Snapshot()
            .Where(l => l.Severity is LogSeverity.Error or LogSeverity.Warning)
            .TakeLast(25)
            .ToList();

        if (errors.Count > 0)
        {
            details.AppendLine("Recent log lines:");
            foreach (var line in errors)
            {
                details.AppendLine($"  {line.Severity}: {line.Text}");
            }
        }

        Write(
            "Job",
            $"FAILED {job.ProjectName} — {job.Title}",
            details: details.ToString());
    }

    public string? ReadAll()
    {
        lock (_lock)
        {
            return File.Exists(_filePath) ? File.ReadAllText(_filePath, Encoding.UTF8) : null;
        }
    }

    public string? ReadTail(int maxLines = 200)
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var lines = File.ReadAllLines(_filePath, Encoding.UTF8);
            if (lines.Length <= maxLines)
            {
                return string.Join(Environment.NewLine, lines);
            }

            return string.Join(Environment.NewLine, lines[^maxLines..]);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var info = new FileInfo(_filePath);
        if (info.Length < MaxBytesBeforeRotate)
        {
            return;
        }

        var rotated = _filePath + ".1";
        if (File.Exists(rotated))
        {
            File.Delete(rotated);
        }

        File.Move(_filePath, rotated);
    }
}
