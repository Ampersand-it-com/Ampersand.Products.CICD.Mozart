using System.Text;
using System.Text.RegularExpressions;
using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services;

public sealed record VersionBumpResult(string FilePath, string OldVersion, string NewVersion);

/// <summary>
/// Bumps Ampersand-style assembly version in Program.cs:
/// [assembly: AssemblyVersion("1.0.x")] / AssemblyFileVersion.
/// Also syncs csproj Version/AssemblyVersion/FileVersion when those tags already exist.
/// </summary>
public sealed class VersionBumpService
{
    private static readonly Regex AssemblyAttr = new(
        @"(\[assembly:\s*Assembly(?:File)?Version\("")(\d+(?:\.\d+){1,3})(""\)\])",
        RegexOptions.Compiled);

    private static readonly Regex CsprojVersion = new(
        @"(<(?:Version|AssemblyVersion|FileVersion)>)(\d+(?:\.\d+){1,3})(</(?:Version|AssemblyVersion|FileVersion)>)",
        RegexOptions.Compiled);

    public VersionBumpResult? BumpForPublish(string ownerProjectPath)
    {
        if (string.IsNullOrWhiteSpace(ownerProjectPath) || !File.Exists(ownerProjectPath))
        {
            return null;
        }

        var program = Path.Combine(Path.GetDirectoryName(ownerProjectPath)!, "Program.cs");
        VersionBumpResult? result = null;

        if (File.Exists(program))
        {
            result = BumpProgram(program);
        }

        result ??= BumpCsproj(ownerProjectPath, nextVersion: null);

        if (result is not null && File.Exists(ownerProjectPath))
        {
            BumpCsproj(ownerProjectPath, result.NewVersion);
        }

        return result;
    }

    public string? ReadVersion(string ownerProjectPath)
    {
        if (string.IsNullOrWhiteSpace(ownerProjectPath))
        {
            return null;
        }

        var program = Path.Combine(Path.GetDirectoryName(ownerProjectPath) ?? ".", "Program.cs");
        if (File.Exists(program))
        {
            var match = AssemblyAttr.Match(ReadUtf8(program).Text);
            if (match.Success)
            {
                return match.Groups[2].Value;
            }
        }

        if (File.Exists(ownerProjectPath))
        {
            var match = CsprojVersion.Match(ReadUtf8(ownerProjectPath).Text);
            if (match.Success)
            {
                return match.Groups[2].Value;
            }
        }

        return null;
    }

    public string? ReadVersion(CicdProject project, IReadOnlyList<PublishProfileInfo> profiles)
    {
        var owner = ResolveOwner(project, profiles);
        return owner is null ? null : ReadVersion(owner);
    }

    public static string? ResolveOwner(CicdProject project, IReadOnlyList<PublishProfileInfo> profiles)
    {
        if (!string.IsNullOrWhiteSpace(project.DefaultPublishProfile))
        {
            var match = profiles.FirstOrDefault(p =>
                string.Equals(p.Name, project.DefaultPublishProfile, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.OwnerProjectPath;
            }
        }

        if (profiles.Count > 0)
        {
            return profiles[0].OwnerProjectPath;
        }

        var files = SolutionReader.GetProjectFiles(project.ProjectPath);
        return files.FirstOrDefault(f => File.Exists(Path.Combine(Path.GetDirectoryName(f) ?? ".", "Program.cs")))
               ?? files.FirstOrDefault();
    }

    private static VersionBumpResult? BumpProgram(string programPath)
    {
        var (text, bom) = ReadUtf8(programPath);
        var matches = AssemblyAttr.Matches(text);
        if (matches.Count == 0)
        {
            return InsertAssemblyVersion(programPath, text, bom);
        }

        var oldVersion = matches[0].Groups[2].Value;
        var newVersion = Increment(oldVersion);
        var updated = AssemblyAttr.Replace(text, m => $"{m.Groups[1].Value}{newVersion}{m.Groups[3].Value}");
        WriteUtf8(programPath, updated, bom);
        return new VersionBumpResult(programPath, oldVersion, newVersion);
    }

    private static VersionBumpResult InsertAssemblyVersion(string programPath, string text, bool bom)
    {
        const string oldVersion = "0.0.0";
        const string newVersion = "1.0.1";
        var block =
            "[assembly: AssemblyVersion(\"" + newVersion + "\")]" + Environment.NewLine +
            "[assembly: AssemblyFileVersion(\"" + newVersion + "\")]" + Environment.NewLine + Environment.NewLine;

        if (!text.Contains("using System.Reflection;", StringComparison.Ordinal))
        {
            block = "using System.Reflection;" + Environment.NewLine + block;
        }

        WriteUtf8(programPath, block + text, bom);
        return new VersionBumpResult(programPath, oldVersion, newVersion);
    }

    private static VersionBumpResult? BumpCsproj(string csprojPath, string? nextVersion)
    {
        var (text, bom) = ReadUtf8(csprojPath);
        var matches = CsprojVersion.Matches(text);
        if (matches.Count == 0)
        {
            return null;
        }

        var oldVersion = matches[0].Groups[2].Value;
        var newVersion = nextVersion ?? Increment(oldVersion);
        if (oldVersion == newVersion)
        {
            return new VersionBumpResult(csprojPath, oldVersion, newVersion);
        }

        var updated = CsprojVersion.Replace(text, m => $"{m.Groups[1].Value}{newVersion}{m.Groups[3].Value}");
        WriteUtf8(csprojPath, updated, bom);
        return new VersionBumpResult(csprojPath, oldVersion, newVersion);
    }

    private static string Increment(string version)
    {
        var parts = version.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[^1], out var last))
        {
            return version + ".1";
        }

        parts[^1] = (last + 1).ToString();
        return string.Join('.', parts);
    }

    private static (string Text, bool Bom) ReadUtf8(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = bom
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.UTF8.GetString(bytes);
        return (text, bom);
    }

    private static void WriteUtf8(string path, string text, bool bom) =>
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
}
