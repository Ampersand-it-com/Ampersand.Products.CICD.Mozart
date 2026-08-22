using System.Xml.Linq;
using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Reads csproj / sln metadata without invoking MSBuild.
/// </summary>
public sealed class ProjectInspector
{
    private static readonly string[] TestMarkers =
    [
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "NUnit",
        "MSTest",
    ];

    private static readonly string[] SkipFolders =
    [
        "bin", "obj", ".git", ".vs", "node_modules", "packages", ".idea",
    ];

    public ProjectInfo Inspect(string path)
    {
        var info = new ProjectInfo { Path = path };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            info.Error = "File not found";
            return info;
        }

        info.Exists = true;
        info.LastWriteUtc = File.GetLastWriteTimeUtc(path);

        var projectFiles = SolutionReader.GetProjectFiles(path);
        info.SolutionProjects = [.. projectFiles.Select(p => Path.GetFileNameWithoutExtension(p))];

        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectFile in projectFiles)
        {
            try
            {
                var document = XDocument.Load(projectFile);
                var root = document.Root;
                info.Sdk ??= root?.Attribute("Sdk")?.Value;

                foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks"))
                {
                    foreach (var framework in element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        frameworks.Add(framework);
                    }
                }

                var outputType = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "OutputType")?.Value;
                info.OutputType ??= outputType;

                foreach (var reference in document.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
                {
                    var name = reference.Attribute("Include")?.Value;
                    if (!string.IsNullOrWhiteSpace(name) && !info.PackageReferences.Contains(name))
                    {
                        info.PackageReferences.Add(name);
                    }
                }

                var sdk = root?.Attribute("Sdk")?.Value ?? string.Empty;
                if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase))
                {
                    info.IsWebProject = true;
                }

                if (info.PackageReferences.Any(p => TestMarkers.Any(m => p.Contains(m, StringComparison.OrdinalIgnoreCase))))
                {
                    info.IsTestProject = true;
                }
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
        }

        info.TargetFramework = frameworks.Count == 0 ? null : string.Join(", ", frameworks);

        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            var (count, size) = MeasureSources(directory);
            info.SourceFileCount = count;
            info.SourceSizeBytes = size;
        }

        return info;
    }

    private static (int Count, long Size) MeasureSources(string directory)
    {
        var count = 0;
        long size = 0;

        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current, "*.cs"))
                {
                    count++;
                    size += new FileInfo(file).Length;
                }

                foreach (var sub in Directory.EnumerateDirectories(current))
                {
                    var name = Path.GetFileName(sub);
                    if (SkipFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    stack.Push(sub);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders the panel cannot read.
            }
        }

        return (count, size);
    }
}
