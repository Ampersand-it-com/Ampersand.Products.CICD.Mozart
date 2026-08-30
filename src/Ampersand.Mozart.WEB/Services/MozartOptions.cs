namespace Ampersand.Mozart.WEB.Services;

public sealed class MozartOptions
{
    public const string SectionName = "Mozart";

    /// <summary>Folder for projects.json. Relative paths resolve against the content root.</summary>
    public string DataDirectory { get; set; } = "data";

    public string DotnetPath { get; set; } = "dotnet";

    public string GitPath { get; set; } = "git";

    public string DefaultConfiguration { get; set; } = "Release";

    /// <summary>How many jobs may run at the same time.</summary>
    public int MaxParallelJobs { get; set; } = 2;

    public int JobHistoryLimit { get; set; } = 200;

    /// <summary>Hard timeout for a single command. Run jobs ignore this.</summary>
    public int JobTimeoutMinutes { get; set; } = 30;

    /// <summary>Optional cap for dotnet run jobs. Zero = no timeout.</summary>
    public int RunTimeoutMinutes { get; set; } = 0;

    public int HealthTimeoutSeconds { get; set; } = 10;

    /// <summary>Background health polling interval. Zero disables polling.</summary>
    public int HealthPollSeconds { get; set; } = 120;

    /// <summary>Folders offered as starting points in the project picker.</summary>
    public List<string> ScanRoots { get; set; } = [];

    public int ScanMaxDepth { get; set; } = 4;
}
