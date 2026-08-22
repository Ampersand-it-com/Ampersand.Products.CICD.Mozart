namespace Ampersand.Mozart.WEB.Models;

/// <summary>
/// Static facts read from csproj/sln files on disk.
/// </summary>
public sealed class ProjectInfo
{
    public string Path { get; set; } = string.Empty;

    public bool Exists { get; set; }

    public string? Sdk { get; set; }

    public string? TargetFramework { get; set; }

    public string? OutputType { get; set; }

    public bool IsTestProject { get; set; }

    public bool IsWebProject { get; set; }

    public List<string> PackageReferences { get; set; } = [];

    public List<string> SolutionProjects { get; set; } = [];

    public long SourceSizeBytes { get; set; }

    public int SourceFileCount { get; set; }

    public DateTimeOffset? LastWriteUtc { get; set; }

    public string? Error { get; set; }
}

public sealed class GitInfo
{
    public bool IsRepository { get; set; }

    public string? Branch { get; set; }

    public string? Remote { get; set; }

    public string? CommitHash { get; set; }

    public string? CommitAuthor { get; set; }

    public string? CommitSubject { get; set; }

    public DateTimeOffset? CommitDate { get; set; }

    public int DirtyFiles { get; set; }

    public int Ahead { get; set; }

    public int Behind { get; set; }

    public string? Error { get; set; }

    public bool IsClean => DirtyFiles == 0;
}

public sealed class HealthResult
{
    public DateTimeOffset CheckedUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? Url { get; set; }

    public int? HttpStatusCode { get; set; }

    public long? HttpElapsedMs { get; set; }

    public string? HttpError { get; set; }

    public string? PingHost { get; set; }

    public string? PingStatus { get; set; }

    public long? PingRoundtripMs { get; set; }

    public bool HttpOk => HttpStatusCode is >= 200 and < 400;

    public bool PingOk => string.Equals(PingStatus, "Success", StringComparison.OrdinalIgnoreCase);

    public bool HasHttp => Url is not null;

    public bool HasPing => PingHost is not null;
}

/// <summary>
/// Candidate found while scanning a folder in the "add project" picker.
/// </summary>
public sealed class DiscoveredProject
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public bool IsWeb { get; set; }

    public bool IsTest { get; set; }

    public string? TargetFramework { get; set; }

    public bool AlreadyRegistered { get; set; }

    public bool Selected { get; set; }
}

public sealed record FolderEntry(string Name, string FullPath, int ProjectFileCount);
