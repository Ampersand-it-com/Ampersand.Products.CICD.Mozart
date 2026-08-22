using System.Diagnostics;
using System.Text;
using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    public string CombinedOutput => string.IsNullOrWhiteSpace(StandardError)
        ? StandardOutput
        : $"{StandardOutput}{Environment.NewLine}{StandardError}";
}

/// <summary>
/// Thin wrapper over <see cref="Process"/> with streaming output and process-tree cancellation.
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger)
{
    public async Task<int> StreamAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<LogSeverity, string> onLine,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(fileName, arguments, workingDirectory);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdoutDone = new TaskCompletionSource();
        var stderrDone = new TaskCompletionSource();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
                return;
            }

            onLine(Classify(e.Data), e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
                return;
            }

            onLine(LogSeverity.Error, e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start {File} {Args}", fileName, arguments);
            onLine(LogSeverity.Error, $"Failed to start process: {ex.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillTree(process, onLine);
            throw;
        }

        await Task.WhenAny(Task.WhenAll(stdoutDone.Task, stderrDone.Task), Task.Delay(2000, CancellationToken.None));
        return process.ExitCode;
    }

    public async Task<ProcessRunResult> CaptureAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var startInfo = CreateStartInfo(fileName, arguments, workingDirectory);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(-1, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillTree(process, null);
            throw;
        }

        return new ProcessRunResult(process.ExitCode, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Keep MSBuild output parseable and free of ANSI escapes.
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["TERM"] = "dumb";
        info.Environment["NO_COLOR"] = "1";
        info.Environment["MSBUILDTERMINALLOGGER"] = "off";
        return info;
    }

    private static void KillTree(Process process, Action<LogSeverity, string>? onLine)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                onLine?.Invoke(LogSeverity.System, "Process tree killed by user request.");
            }
        }
        catch (Exception ex)
        {
            onLine?.Invoke(LogSeverity.Error, $"Failed to kill process: {ex.Message}");
        }
    }

    private static LogSeverity Classify(string line)
    {
        if (line.Contains(": error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Failed!", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("error", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Error;
        }

        if (line.Contains(": warning", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("warn", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Warning;
        }

        return LogSeverity.Info;
    }
}
