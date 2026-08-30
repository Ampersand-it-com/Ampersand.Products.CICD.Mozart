using System.Text.Json;
using System.Xml.Linq;
using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Resolves dotnet run targets and launch profiles from launchSettings.json.
/// </summary>
public sealed class LaunchSettingsService
{
    public IReadOnlyList<LaunchProfileInfo> GetProfiles(string csprojPath)
    {
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
        {
            return [];
        }

        var propertiesDir = Path.Combine(Path.GetDirectoryName(csprojPath)!, "Properties");
        var settingsPath = Path.Combine(propertiesDir, "launchSettings.json");
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (!document.RootElement.TryGetProperty("profiles", out var profiles))
            {
                return [];
            }

            var result = new List<LaunchProfileInfo>();
            foreach (var entry in profiles.EnumerateObject())
            {
                if (!IsRunnableProfile(entry.Value))
                {
                    continue;
                }

                string? applicationUrl = null;
                string? launchUrl = null;
                if (entry.Value.TryGetProperty("applicationUrl", out var appUrlElement))
                {
                    applicationUrl = appUrlElement.GetString();
                }

                if (entry.Value.TryGetProperty("launchUrl", out var launchUrlElement))
                {
                    launchUrl = launchUrlElement.GetString();
                }

                result.Add(new LaunchProfileInfo
                {
                    Name = entry.Name,
                    ApplicationUrl = applicationUrl,
                    BrowserUrl = FirstHttpUrl(applicationUrl) ?? FirstHttpUrl(launchUrl),
                    OwnerProjectPath = csprojPath,
                });
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    public IReadOnlyList<LaunchProfileInfo> GetProfiles(CicdProject project)
    {
        var target = ResolveRunProjectPath(project);
        if (target is null)
        {
            return [];
        }

        return GetProfiles(target);
    }

    public string? ResolveRunProjectPath(CicdProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.RunProjectPath) && File.Exists(project.RunProjectPath))
        {
            return project.RunProjectPath;
        }

        var entry = project.ProjectPath;
        if (string.IsNullOrWhiteSpace(entry) || !File.Exists(entry))
        {
            return null;
        }

        var extension = Path.GetExtension(entry);
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            return CanRunProject(entry) ? entry : null;
        }

        foreach (var projectFile in SolutionReader.GetProjectFiles(entry))
        {
            if (CanRunProject(projectFile))
            {
                return projectFile;
            }
        }

        return null;
    }

    public LaunchProfileInfo? ResolveDefaultProfile(CicdProject project, IReadOnlyList<LaunchProfileInfo> profiles)
    {
        if (profiles.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(project.DefaultLaunchProfile))
        {
            var configured = profiles.FirstOrDefault(
                p => string.Equals(p.Name, project.DefaultLaunchProfile, StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
            {
                return configured;
            }
        }

        foreach (var preferred in new[] { "http", "https", "Development", "Project" })
        {
            var match = profiles.FirstOrDefault(
                p => string.Equals(p.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return profiles[0];
    }

    public bool CanRun(CicdProject project) => ResolveRunProjectPath(project) is not null;

    private static bool CanRunProject(string csprojPath)
    {
        if (HasLaunchSettings(csprojPath))
        {
            return true;
        }

        try
        {
            var document = XDocument.Load(csprojPath);
            var root = document.Root;
            var sdk = root?.Attribute("Sdk")?.Value ?? string.Empty;
            if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var outputType = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "OutputType")
                ?.Value;

            return string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool HasLaunchSettings(string csprojPath)
    {
        var settingsPath = Path.Combine(Path.GetDirectoryName(csprojPath)!, "Properties", "launchSettings.json");
        return File.Exists(settingsPath);
    }

    private static bool IsRunnableProfile(JsonElement profile)
    {
        if (profile.TryGetProperty("commandName", out var commandName))
        {
            return string.Equals(commandName.GetString(), "Project", StringComparison.OrdinalIgnoreCase);
        }

        // Older templates may omit commandName; treat as runnable when applicationUrl exists.
        return profile.TryGetProperty("applicationUrl", out _);
    }

    private static string? FirstHttpUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(part, UriKind.Absolute, out var uri) &&
                uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }
        }

        return null;
    }
}
