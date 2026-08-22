using System.Globalization;
using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Reads repository state for the project cards (branch, commit, dirty files, ahead/behind).
/// </summary>
public sealed class GitService(ProcessRunner runner, IOptions<MozartOptions> options)
{
    private readonly MozartOptions _options = options.Value;

    public async Task<GitInfo> GetInfoAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        var info = new GitInfo();

        if (!Directory.Exists(workingDirectory))
        {
            info.Error = "Working directory not found";
            return info;
        }

        var top = await RunAsync(workingDirectory, "rev-parse --show-toplevel", cancellationToken);
        if (!top.Success)
        {
            info.Error = string.IsNullOrWhiteSpace(top.StandardError) ? "Not a git repository" : top.StandardError.Trim();
            return info;
        }

        info.IsRepository = true;

        var branch = await RunAsync(workingDirectory, "rev-parse --abbrev-ref HEAD", cancellationToken);
        info.Branch = branch.Success ? branch.StandardOutput.Trim() : null;

        var remote = await RunAsync(workingDirectory, "config --get remote.origin.url", cancellationToken);
        info.Remote = remote.Success && remote.StandardOutput.Length > 0 ? remote.StandardOutput.Trim() : null;

        var commit = await RunAsync(workingDirectory, "log -1 --pretty=format:%h%x1f%an%x1f%aI%x1f%s", cancellationToken);
        if (commit.Success)
        {
            var parts = commit.StandardOutput.Trim().Split('\u001f');
            if (parts.Length == 4)
            {
                info.CommitHash = parts[0];
                info.CommitAuthor = parts[1];
                info.CommitDate = DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null;
                info.CommitSubject = parts[3];
            }
        }

        var status = await RunAsync(workingDirectory, "status --porcelain", cancellationToken);
        if (status.Success)
        {
            info.DirtyFiles = status.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;
        }

        var tracking = await RunAsync(workingDirectory, "rev-list --left-right --count @{u}...HEAD", cancellationToken);
        if (tracking.Success)
        {
            var counters = tracking.StandardOutput.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (counters.Length == 2 &&
                int.TryParse(counters[0].Trim(), out var behind) &&
                int.TryParse(counters[1].Trim(), out var ahead))
            {
                info.Behind = behind;
                info.Ahead = ahead;
            }
        }

        return info;
    }

    private Task<ProcessRunResult> RunAsync(string workingDirectory, string arguments, CancellationToken cancellationToken) =>
        runner.CaptureAsync(_options.GitPath, arguments, workingDirectory, cancellationToken);
}
