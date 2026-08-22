using System.Text.Json;
using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// JSON-backed registry of managed projects.
/// </summary>
public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<ProjectStore> _logger;
    private readonly string _filePath;
    private List<CicdProject> _projects = [];
    private bool _loaded;

    public ProjectStore(IOptions<MozartOptions> options, IHostEnvironment environment, ILogger<ProjectStore> logger)
    {
        _logger = logger;
        var configured = options.Value.DataDirectory;
        var directory = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "projects.json");
    }

    public string FilePath => _filePath;

    public event Action? Changed;

    public async Task<IReadOnlyList<CicdProject>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return [.. _projects.OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<CicdProject?> GetAsync(string id)
    {
        await EnsureLoadedAsync();
        return _projects.FirstOrDefault(p => p.Id == id);
    }

    public async Task<CicdProject> SaveAsync(CicdProject project)
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        try
        {
            var existing = _projects.FindIndex(p => p.Id == project.Id);
            if (existing >= 0)
            {
                _projects[existing] = project;
            }
            else
            {
                if (project.SortOrder == 0)
                {
                    project.SortOrder = _projects.Count == 0 ? 1 : _projects.Max(p => p.SortOrder) + 1;
                }

                _projects.Add(project);
            }

            await PersistAsync();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
        return project;
    }

    public async Task<int> AddRangeAsync(IEnumerable<CicdProject> projects)
    {
        await EnsureLoadedAsync();
        var added = 0;
        await _gate.WaitAsync();
        try
        {
            var order = _projects.Count == 0 ? 0 : _projects.Max(p => p.SortOrder);
            foreach (var project in projects)
            {
                if (_projects.Any(p => string.Equals(p.ProjectPath, project.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                project.SortOrder = ++order;
                _projects.Add(project);
                added++;
            }

            if (added > 0)
            {
                await PersistAsync();
            }
        }
        finally
        {
            _gate.Release();
        }

        if (added > 0)
        {
            Changed?.Invoke();
        }

        return added;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        bool removed;
        try
        {
            removed = _projects.RemoveAll(p => p.Id == id) > 0;
            if (removed)
            {
                await PersistAsync();
            }
        }
        finally
        {
            _gate.Release();
        }

        if (removed)
        {
            Changed?.Invoke();
        }

        return removed;
    }

    public async Task MoveAsync(string id, int delta)
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        try
        {
            var ordered = _projects
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var index = ordered.FindIndex(p => p.Id == id);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= ordered.Count)
            {
                return;
            }

            (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].SortOrder = i + 1;
            }

            _projects = ordered;
            await PersistAsync();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_loaded)
            {
                return;
            }

            if (File.Exists(_filePath))
            {
                try
                {
                    await using var stream = File.OpenRead(_filePath);
                    _projects = await JsonSerializer.DeserializeAsync<List<CicdProject>>(stream, SerializerOptions) ?? [];
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read project store at {Path}", _filePath);
                    _projects = [];
                }
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistAsync()
    {
        var temp = _filePath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, _projects, SerializerOptions);
        }

        File.Move(temp, _filePath, overwrite: true);
    }
}
