namespace Ampersand.Mozart.WEB.Models;

public enum PublishMethod
{
    FileSystem,
    MsDeploy,
    Package,
    Unknown,
}

/// <summary>
/// Parsed representation of a .pubxml publish profile.
/// </summary>
public sealed class PublishProfileInfo
{
    public string Name { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    /// <summary>Project the profile belongs to (a solution can hold many).</summary>
    public string OwnerProjectPath { get; set; } = string.Empty;

    public PublishMethod Method { get; set; } = PublishMethod.Unknown;

    public string Configuration { get; set; } = "Release";

    public string? TargetFramework { get; set; }

    public string? RuntimeIdentifier { get; set; }

    public bool SelfContained { get; set; }

    /// <summary>Output folder for FileSystem profiles.</summary>
    public string? PublishDirectory { get; set; }

    /// <summary>Web Deploy endpoint, e.g. https://server:8172/msdeploy.axd.</summary>
    public string? MsDeployServiceUrl { get; set; }

    public string? DeployIisAppPath { get; set; }

    public string? UserName { get; set; }

    public bool AllowUntrustedCertificate { get; set; } = true;

    public string? SiteUrlToLaunchAfterPublish { get; set; }

    public string RawXml { get; set; } = string.Empty;

    public Dictionary<string, string> Properties { get; set; } = [];

    public string MethodLabel => Method switch
    {
        PublishMethod.FileSystem => "Folder",
        PublishMethod.MsDeploy => "Web Deploy",
        PublishMethod.Package => "Package",
        _ => "Unknown",
    };

    public string TargetLabel => Method switch
    {
        PublishMethod.MsDeploy => $"{MsDeployServiceUrl} → {DeployIisAppPath}",
        _ => PublishDirectory ?? "-",
    };

    public bool IsUsable => Method switch
    {
        PublishMethod.MsDeploy =>
            !string.IsNullOrWhiteSpace(MsDeployServiceUrl) &&
            !string.IsNullOrWhiteSpace(DeployIisAppPath),
        PublishMethod.FileSystem => !string.IsNullOrWhiteSpace(PublishDirectory),
        PublishMethod.Package => true,
        _ => false,
    };
}
