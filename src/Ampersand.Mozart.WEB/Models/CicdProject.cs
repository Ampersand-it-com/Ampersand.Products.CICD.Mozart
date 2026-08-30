namespace Ampersand.Mozart.WEB.Models;

/// <summary>
/// Registered project managed by the panel. Persisted to the local JSON store.
/// </summary>
public sealed class CicdProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to .sln, .slnx or .csproj.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Optional separate test project/solution path. Falls back to <see cref="ProjectPath"/>.</summary>
    public string? TestPath { get; set; }

    public string Configuration { get; set; } = "Release";

    /// <summary>Publish profile name used by the one-click deploy button.</summary>
    public string? DefaultPublishProfile { get; set; }

    /// <summary>launchSettings profile for dotnet run (e.g. http).</summary>
    public string? DefaultLaunchProfile { get; set; }

    /// <summary>Explicit csproj to run when the entry point is a solution.</summary>
    public string? RunProjectPath { get; set; }

    /// <summary>URL probed by the health check (HTTP status + latency).</summary>
    public string? HealthUrl { get; set; }

    /// <summary>Host or IP for ICMP ping. Derived from <see cref="HealthUrl"/> when empty.</summary>
    public string? PingHost { get; set; }

    public string? Group { get; set; }

    public List<string> Tags { get; set; } = [];

    public string? Notes { get; set; }

    public int SortOrder { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Extra arguments appended to every dotnet command for this project.</summary>
    public string? ExtraArguments { get; set; }

    /// <summary>Web Deploy passwords per publish profile. Kept outside of the repository.</summary>
    public Dictionary<string, string> PublishSecrets { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastDeployUtc { get; set; }

    /// <summary>Last assembly version written on deploy.</summary>
    public string? LastVersion { get; set; }

    public string? LastDeployError { get; set; }

    public string WorkingDirectory =>
        Directory.Exists(ProjectPath)
            ? ProjectPath
            : Path.GetDirectoryName(ProjectPath) ?? Environment.CurrentDirectory;

    public bool IsSolution =>
        ProjectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        ProjectPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? Path.GetFileNameWithoutExtension(ProjectPath)
        : Name;

    public CicdProject Clone() => new()
    {
        Id = Id,
        Name = Name,
        ProjectPath = ProjectPath,
        TestPath = TestPath,
        Configuration = Configuration,
        DefaultPublishProfile = DefaultPublishProfile,
        DefaultLaunchProfile = DefaultLaunchProfile,
        RunProjectPath = RunProjectPath,
        HealthUrl = HealthUrl,
        PingHost = PingHost,
        Group = Group,
        Tags = [.. Tags],
        Notes = Notes,
        SortOrder = SortOrder,
        IsEnabled = IsEnabled,
        ExtraArguments = ExtraArguments,
        PublishSecrets = new Dictionary<string, string>(PublishSecrets),
        CreatedUtc = CreatedUtc,
        LastDeployUtc = LastDeployUtc,
        LastVersion = LastVersion,
        LastDeployError = LastDeployError,
    };
}
