using System.Xml.Linq;
using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Reads and writes Visual Studio publish profiles (Properties/PublishProfiles/*.pubxml).
/// </summary>
public sealed class PublishProfileService(ILogger<PublishProfileService> logger)
{
    private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";

    public IReadOnlyList<PublishProfileInfo> GetProfiles(CicdProject project)
    {
        var result = new List<PublishProfileInfo>();

        foreach (var projectFile in SolutionReader.GetProjectFiles(project.ProjectPath))
        {
            var folder = GetProfileFolder(projectFile);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.pubxml", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var parsed = Parse(file, projectFile);
                    if (!parsed.IsUsable)
                    {
                        logger.LogInformation("Skipping empty publish profile {File}", file);
                        continue;
                    }

                    result.Add(parsed);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse publish profile {File}", file);
                }
            }
        }

        return [.. result
            .OrderBy(p => p.Method == PublishMethod.MsDeploy ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public PublishProfileInfo? Find(CicdProject project, string profileName) =>
        GetProfiles(project).FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

    public static string GetProfileFolder(string projectFile) =>
        Path.Combine(Path.GetDirectoryName(projectFile) ?? ".", "Properties", "PublishProfiles");

    public PublishProfileInfo Save(PublishProfileInfo profile)
    {
        var folder = GetProfileFolder(profile.OwnerProjectPath);
        Directory.CreateDirectory(folder);

        var path = string.IsNullOrWhiteSpace(profile.FilePath)
            ? Path.Combine(folder, $"{Sanitize(profile.Name)}.pubxml")
            : profile.FilePath;

        var properties = new List<(string Key, string? Value)>
        {
            ("DeleteExistingFiles", "false"),
            ("ExcludeApp_Data", "false"),
            ("LaunchSiteAfterPublish", "false"),
            ("LastUsedBuildConfiguration", profile.Configuration),
            ("LastUsedPlatform", "Any CPU"),
            ("PublishProvider", profile.Method == PublishMethod.FileSystem ? "FileSystem" : null),
            ("PublishUrl", profile.Method == PublishMethod.FileSystem ? profile.PublishDirectory : null),
            ("publishUrl", null),
            ("WebPublishMethod", profile.Method switch
            {
                PublishMethod.FileSystem => "FileSystem",
                PublishMethod.MsDeploy => "MSDeploy",
                PublishMethod.Package => "Package",
                _ => "FileSystem",
            }),
            ("TargetFramework", profile.TargetFramework),
            ("RuntimeIdentifier", profile.RuntimeIdentifier),
            ("SelfContained", profile.SelfContained ? "true" : "false"),
            ("PublishDir", profile.Method == PublishMethod.FileSystem ? profile.PublishDirectory : null),
            ("MSDeployServiceURL", profile.Method == PublishMethod.MsDeploy ? profile.MsDeployServiceUrl : null),
            ("DeployIisAppPath", profile.Method == PublishMethod.MsDeploy ? profile.DeployIisAppPath : null),
            ("RemoteSitePhysicalPath", null),
            ("SkipExtraFilesOnServer", profile.Method == PublishMethod.MsDeploy ? "false" : null),
            ("MSDeployPublishMethod", profile.Method == PublishMethod.MsDeploy ? "WMSVC" : null),
            ("EnableMSDeployBackup", profile.Method == PublishMethod.MsDeploy ? "false" : null),
            ("AllowUntrustedCertificate", profile.Method == PublishMethod.MsDeploy
                ? profile.AllowUntrustedCertificate ? "true" : "false"
                : null),
            ("UserName", profile.Method == PublishMethod.MsDeploy ? profile.UserName : null),
            ("_SavePWD", profile.Method == PublishMethod.MsDeploy ? "false" : null),
            ("SiteUrlToLaunchAfterPublish", profile.SiteUrlToLaunchAfterPublish),
        };

        var group = new XElement(MsBuild + "PropertyGroup");
        foreach (var (key, value) in properties)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                group.Add(new XElement(MsBuild + key, value));
            }
        }

        var document = new XDocument(
            new XComment("Managed by Mozart CI/CD panel. Safe to edit by hand."),
            new XElement(
                MsBuild + "Project",
                new XAttribute("ToolsVersion", "4.0"),
                group));

        document.Save(path);

        profile.FilePath = path;
        profile.RawXml = File.ReadAllText(path);
        return profile;
    }

    public void SaveRaw(string filePath, string xml)
    {
        // Validate before overwriting so a typo cannot corrupt the profile.
        XDocument.Parse(xml);
        File.WriteAllText(filePath, xml);
    }

    public bool Delete(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        File.Delete(filePath);
        var userFile = filePath + ".user";
        if (File.Exists(userFile))
        {
            File.Delete(userFile);
        }

        return true;
    }

    private static PublishProfileInfo Parse(string file, string ownerProjectPath)
    {
        var document = XDocument.Load(file);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "PropertyGroup").Elements())
        {
            properties[element.Name.LocalName] = element.Value.Trim();
        }

        var method = properties.GetValueOrDefault("WebPublishMethod") ?? properties.GetValueOrDefault("PublishProvider");

        return new PublishProfileInfo
        {
            Name = Path.GetFileNameWithoutExtension(file),
            FilePath = file,
            OwnerProjectPath = ownerProjectPath,
            Method = method?.ToLowerInvariant() switch
            {
                "filesystem" => PublishMethod.FileSystem,
                "msdeploy" => PublishMethod.MsDeploy,
                "package" => PublishMethod.Package,
                _ => PublishMethod.Unknown,
            },
            Configuration = properties.GetValueOrDefault("LastUsedBuildConfiguration") ?? "Release",
            TargetFramework = properties.GetValueOrDefault("TargetFramework"),
            RuntimeIdentifier = properties.GetValueOrDefault("RuntimeIdentifier"),
            SelfContained = string.Equals(properties.GetValueOrDefault("SelfContained"), "true", StringComparison.OrdinalIgnoreCase),
            PublishDirectory = properties.GetValueOrDefault("PublishDir") ?? properties.GetValueOrDefault("PublishUrl"),
            MsDeployServiceUrl = properties.GetValueOrDefault("MSDeployServiceURL"),
            DeployIisAppPath = properties.GetValueOrDefault("DeployIisAppPath"),
            UserName = properties.GetValueOrDefault("UserName"),
            AllowUntrustedCertificate = !string.Equals(properties.GetValueOrDefault("AllowUntrustedCertificate"), "false", StringComparison.OrdinalIgnoreCase),
            SiteUrlToLaunchAfterPublish = properties.GetValueOrDefault("SiteUrlToLaunchAfterPublish"),
            RawXml = File.ReadAllText(file),
            Properties = properties,
        };
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Where(c => !invalid.Contains(c))]).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Profile" : cleaned;
    }
}
