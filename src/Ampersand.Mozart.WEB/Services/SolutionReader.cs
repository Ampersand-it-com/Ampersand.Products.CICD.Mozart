using System.Xml.Linq;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Resolves the csproj files behind a .sln / .slnx entry point.
/// </summary>
public static class SolutionReader
{
    public static IReadOnlyList<string> GetProjectFiles(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath) || !File.Exists(entryPath))
        {
            return [];
        }

        var extension = Path.GetExtension(entryPath).ToLowerInvariant();
        return extension switch
        {
            ".csproj" or ".fsproj" or ".vbproj" => [entryPath],
            ".sln" => ReadSln(entryPath),
            ".slnx" => ReadSlnx(entryPath),
            _ => [],
        };
    }

    private static List<string> ReadSln(string slnPath)
    {
        var root = Path.GetDirectoryName(slnPath) ?? ".";
        var result = new List<string>();

        foreach (var line in File.ReadLines(slnPath))
        {
            if (!line.StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length < 2)
            {
                continue;
            }

            var fields = parts[1].Split(',');
            if (fields.Length < 2)
            {
                continue;
            }

            var relative = fields[1].Trim().Trim('"');
            if (!IsProjectFile(relative))
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (File.Exists(full))
            {
                result.Add(full);
            }
        }

        return result;
    }

    private static List<string> ReadSlnx(string slnxPath)
    {
        var root = Path.GetDirectoryName(slnxPath) ?? ".";
        var result = new List<string>();

        try
        {
            var document = XDocument.Load(slnxPath);
            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "Project"))
            {
                var relative = element.Attribute("Path")?.Value;
                if (string.IsNullOrWhiteSpace(relative) || !IsProjectFile(relative))
                {
                    continue;
                }

                var full = Path.GetFullPath(Path.Combine(root, relative));
                if (File.Exists(full))
                {
                    result.Add(full);
                }
            }
        }
        catch (Exception)
        {
            return result;
        }

        return result;
    }

    private static bool IsProjectFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
}
