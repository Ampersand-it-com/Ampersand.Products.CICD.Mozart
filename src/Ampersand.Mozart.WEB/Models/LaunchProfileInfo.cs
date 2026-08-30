namespace Ampersand.Mozart.WEB.Models;

/// <summary>
/// Entry from Properties/launchSettings.json suitable for dotnet run.
/// </summary>
public sealed class LaunchProfileInfo
{
    public required string Name { get; init; }

    public string? ApplicationUrl { get; init; }

    /// <summary>First HTTP(S) URL from applicationUrl or launchUrl.</summary>
    public string? BrowserUrl { get; init; }

    public required string OwnerProjectPath { get; init; }
}
