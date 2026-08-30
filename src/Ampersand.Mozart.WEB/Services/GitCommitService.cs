using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// git add -A → commit (auto message) → push for Mozart jobs.
/// </summary>
public sealed class GitCommitService(IOptions<MozartOptions> options)
{
    private readonly string _gitPath = options.Value.GitPath;

    public static string BuildCommitMessage(string projectName) =>
        $"Mozart: {projectName} @ {DateTimeOffset.Now:yyyy-MM-dd HH:mm}";

    public async Task<int> ExecuteAsync(
        Job job,
        ProcessRunner runner,
        Action<LogSeverity, string> onLine,
        CancellationToken cancellationToken)
    {
        var workingDirectory = job.WorkingDirectory;
        var message = string.IsNullOrWhiteSpace(job.Arguments)
            ? BuildCommitMessage(job.ProjectName)
            : job.Arguments;

        async Task<int> RunGitAsync(string arguments)
        {
            onLine(LogSeverity.System, $"> git {arguments}");
            return await runner.StreamAsync(
                _gitPath,
                arguments,
                workingDirectory,
                (severity, line) => onLine(severity, line),
                cancellationToken);
        }

        var addCode = await RunGitAsync("add -A");
        if (addCode != 0)
        {
            return addCode;
        }

        var status = await runner.CaptureAsync(_gitPath, "status --porcelain", workingDirectory, cancellationToken);
        if (!status.Success)
        {
            onLine(LogSeverity.Error, string.IsNullOrWhiteSpace(status.StandardError) ? "git status failed." : status.StandardError);
            return status.ExitCode;
        }

        var hasLocalChanges = !string.IsNullOrWhiteSpace(status.StandardOutput);
        if (hasLocalChanges)
        {
            var commitCode = await RunGitAsync($"commit -m {QuoteGitMessage(message)}");
            if (commitCode != 0)
            {
                return commitCode;
            }
        }
        else
        {
            onLine(LogSeverity.System, "No local changes to commit.");
        }

        var ahead = await ReadAheadCountAsync(runner, workingDirectory, cancellationToken);
        if (!hasLocalChanges && ahead <= 0)
        {
            onLine(LogSeverity.System, "Repository is clean and up to date with remote.");
            return 0;
        }

        return await RunGitAsync("push");
    }

    private async Task<int> ReadAheadCountAsync(ProcessRunner runner, string workingDirectory, CancellationToken cancellationToken)
    {
        var ahead = await runner.CaptureAsync(_gitPath, "rev-list --count @{u}..HEAD", workingDirectory, cancellationToken);
        if (!ahead.Success)
        {
            return 0;
        }

        return int.TryParse(ahead.StandardOutput.Trim(), out var count) ? count : 0;
    }

    private static string QuoteGitMessage(string message) =>
        "\"" + message.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
