using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

public sealed class JobRecord
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public int? ExitCode { get; set; }
    public string? Reason { get; set; }
    public string? Summary { get; set; }
    public string Command { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string LogFile { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
}

/// <summary>
/// Append-only job logs on disk. Written from the first line so a crash still leaves a file.
/// </summary>
public sealed class JobLogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();
    private readonly Lock _indexLock = new();
    private readonly string _directory;
    private readonly string _indexPath;
    private readonly int _keep;

    public JobLogStore(IOptions<MozartOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.DataDirectory;
        var root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
        _directory = Path.Combine(root, "jobs");
        Directory.CreateDirectory(_directory);
        _indexPath = Path.Combine(_directory, "index.json");
        _keep = Math.Max(50, options.Value.JobHistoryLimit);
    }

    public string DirectoryPath => _directory;

    public void Start(Job job)
    {
        var file = Path.Combine(_directory, $"{job.Id}.log");
        job.LogFilePath = file;

        var writer = new StreamWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
        {
            AutoFlush = true,
        };
        writer.WriteLine($"Title: {job.Title}");
        writer.WriteLine($"Project: {job.ProjectName}");
        writer.WriteLine($"Status: {job.Status}");
        writer.WriteLine($"Command: {job.FileName} {job.DisplayArguments}");
        writer.WriteLine($"Cwd: {job.WorkingDirectory}");
        writer.WriteLine("---");
        _writers[job.Id] = writer;

        Upsert(ToRecord(job, file));
    }

    public void Append(Job job, LogSeverity severity, string text)
    {
        if (_writers.TryGetValue(job.Id, out var writer))
        {
            writer.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} {severity} {text}");
        }
    }

    public void Complete(Job job)
    {
        if (_writers.TryRemove(job.Id, out var writer))
        {
            writer.WriteLine("---");
            writer.WriteLine($"Status: {job.Status}");
            writer.WriteLine($"Exit: {job.ExitCode}");
            writer.WriteLine($"Reason: {job.Diagnose()}");
            writer.Dispose();
        }

        var file = job.LogFilePath ?? Path.Combine(_directory, $"{job.Id}.log");
        job.LogFilePath = file;
        Upsert(ToRecord(job, file));

        var latest = Path.Combine(_directory, $"last-{Sanitize(job.ProjectName)}.log");
        try
        {
            File.Copy(file, latest, overwrite: true);
        }
        catch
        {
            // Latest copy is convenience only.
        }
    }

    public IReadOnlyList<JobRecord> List(int take = 200)
    {
        lock (_indexLock)
        {
            return [.. LoadIndex().Take(take)];
        }
    }

    public JobRecord? Get(string id)
    {
        lock (_indexLock)
        {
            return LoadIndex().FirstOrDefault(j => j.Id == id);
        }
    }

    public string? ReadText(string id)
    {
        var record = Get(id);
        var path = record?.LogFile ?? Path.Combine(_directory, $"{id}.log");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public IReadOnlyList<JobLogLine> ReadLines(string id)
    {
        var text = ReadText(id);
        if (text is null)
        {
            return [];
        }

        var lines = new List<JobLogLine>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length >= 12 && line[2] == ':' && line[5] == ':')
            {
                var severity = LogSeverity.Info;
                var rest = line[9..];
                foreach (var name in Enum.GetNames<LogSeverity>())
                {
                    if (rest.StartsWith(name + " ", StringComparison.Ordinal))
                    {
                        severity = Enum.Parse<LogSeverity>(name);
                        rest = rest[(name.Length + 1)..];
                        break;
                    }
                }

                lines.Add(new JobLogLine(DateTimeOffset.Now, severity, rest));
            }
            else if (line.Length > 0 && line != "---")
            {
                lines.Add(new JobLogLine(DateTimeOffset.Now, LogSeverity.System, line));
            }
        }

        return lines;
    }

    private void Upsert(JobRecord record)
    {
        lock (_indexLock)
        {
            var list = LoadIndex();
            list.RemoveAll(j => j.Id == record.Id);
            list.Insert(0, record);

            if (list.Count > _keep)
            {
                foreach (var stale in list.Skip(_keep).ToList())
                {
                    list.Remove(stale);
                    if (!string.IsNullOrWhiteSpace(stale.LogFile) && File.Exists(stale.LogFile))
                    {
                        try { File.Delete(stale.LogFile); } catch { /* keep going */ }
                    }
                }
            }

            var temp = _indexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list, JsonOptions));
            File.Move(temp, _indexPath, overwrite: true);
        }
    }

    private List<JobRecord> LoadIndex()
    {
        if (!File.Exists(_indexPath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<JobRecord>>(File.ReadAllText(_indexPath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static JobRecord ToRecord(Job job, string file) => new()
    {
        Id = job.Id,
        ProjectId = job.ProjectId,
        ProjectName = job.ProjectName,
        Kind = job.Kind.ToString(),
        Title = job.Title,
        Status = job.Status.ToString(),
        ExitCode = job.ExitCode,
        Reason = job.Diagnose(),
        Summary = job.Summary,
        Command = $"{job.FileName} {job.DisplayArguments}",
        WorkingDirectory = job.WorkingDirectory,
        LogFile = file,
        CreatedUtc = job.CreatedUtc,
        StartedUtc = job.StartedUtc,
        FinishedUtc = job.FinishedUtc,
    };

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. value.Where(c => !invalid.Contains(c))]);
    }
}
