using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Translates panel buttons into dotnet/git commands and pushes them onto the job queue.
/// </summary>
public sealed class ProjectOperationsService(
    JobManager jobs,
    ProjectStore store,
    PublishProfileService publishProfiles,
    VersionBumpService versions,
    PublishCredentialService credentials,
    MozartErrorLog errorLog,
    GitService git,
    IOptions<MozartOptions> options)
{
    private const string PasswordMask = "***";

    private readonly MozartOptions _options = options.Value;

    public Job Restore(CicdProject project) =>
        Enqueue(project, JobKind.Restore, "Restore", _options.DotnetPath, $"restore {Quote(project.ProjectPath)}{Extra(project)}");

    public Job Build(CicdProject project) =>
        Enqueue(
            project,
            JobKind.Build,
            $"Build ({Configuration(project)})",
            _options.DotnetPath,
            $"build {Quote(project.ProjectPath)} -c {Configuration(project)} --nologo{Extra(project)}");

    public Job Clean(CicdProject project) =>
        Enqueue(
            project,
            JobKind.Clean,
            "Clean",
            _options.DotnetPath,
            $"clean {Quote(project.ProjectPath)} -c {Configuration(project)}{Extra(project)}");

    public Job Test(CicdProject project)
    {
        var target = string.IsNullOrWhiteSpace(project.TestPath) ? project.ProjectPath : project.TestPath!;
        return Enqueue(
            project,
            JobKind.Test,
            "Tests",
            _options.DotnetPath,
            $"test {Quote(target)} -c {Configuration(project)} --nologo --logger \"console;verbosity=normal\"{Extra(project)}");
    }

    public Job GitPull(CicdProject project) =>
        Enqueue(project, JobKind.GitPull, "git pull --ff-only", _options.GitPath, "pull --ff-only");

    public Job GitCommitPush(CicdProject project)
    {
        var message = GitCommitService.BuildCommitMessage(project.DisplayName);
        return jobs.Enqueue(new Job
        {
            ProjectId = project.Id,
            ProjectName = project.DisplayName,
            Kind = JobKind.GitCommitPush,
            Title = "git commit + push",
            FileName = _options.GitPath,
            Arguments = message,
            DisplayArguments = message,
            WorkingDirectory = project.WorkingDirectory,
        });
    }

    /// <summary>
    /// Queues git commit+push for enabled repos with local changes or unpushed commits.
    /// </summary>
    public async Task<DeployAllResult> CommitAllAsync(IReadOnlyList<CicdProject> projects)
    {
        var jobs = new List<Job>();
        var skipped = new List<string>();

        foreach (var project in projects
                     .Where(p => p.IsEnabled)
                     .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = await git.GetInfoAsync(project.WorkingDirectory);
                if (!info.IsRepository)
                {
                    skipped.Add($"{project.DisplayName}: not a git repo");
                    continue;
                }

                if (info.IsClean && info.Ahead <= 0)
                {
                    skipped.Add($"{project.DisplayName}: clean");
                    continue;
                }

                jobs.Add(GitCommitPush(project));
            }
            catch (Exception ex)
            {
                skipped.Add($"{project.DisplayName}: {ex.Message}");
                errorLog.Write("CommitAll", $"Could not queue commit for {project.DisplayName}", ex);
            }
        }

        return new DeployAllResult(jobs, skipped);
    }

    public Job Custom(CicdProject project, string arguments) =>
        Enqueue(project, JobKind.Custom, $"dotnet {arguments}", _options.DotnetPath, arguments);

    /// <summary>
    /// Publishes with a .pubxml profile. Folder profiles go through dotnet publish,
    /// Web Deploy profiles need MSBuild with DeployOnBuild so msdeploy runs.
    /// </summary>
    public async Task<Job> PublishAsync(CicdProject project, string profileName)
    {
        var profile = publishProfiles.Find(project, profileName)
            ?? throw new InvalidOperationException($"Publish profile '{profileName}' not found.");

        var bump = versions.BumpForPublish(profile.OwnerProjectPath);
        if (bump is not null)
        {
            project.LastVersion = bump.NewVersion;
        }

        if (!profile.IsUsable)
        {
            throw new InvalidOperationException(
                $"Publish profile '{profile.Name}' is empty or incomplete. Pick a Web Deploy / folder profile with a real target.");
        }

        var configuration = string.IsNullOrWhiteSpace(profile.Configuration) ? Configuration(project) : profile.Configuration;
        var target = Quote(profile.OwnerProjectPath);
        var profileArg = Quote(profile.Name);
        var profilePath = Quote(profile.FilePath);

        // SDK web projects: `msbuild -p:DeployOnBuild=true` often only builds.
        // `dotnet publish` + full pubxml path actually runs Web Deploy / folder publish.
        var arguments =
            $"publish {target} -c {configuration} -v n --nologo " +
            $"-p:PublishProfile={profileArg} -p:PublishProfileFullPath={profilePath}";
        var displayArguments = arguments;

        if (profile.Method == PublishMethod.MsDeploy)
        {
            arguments += " -p:WebPublishMethod=MSDeploy -p:AllowUntrustedCertificate=true";
            displayArguments += " -p:WebPublishMethod=MSDeploy -p:AllowUntrustedCertificate=true";

            if (!string.IsNullOrWhiteSpace(profile.UserName))
            {
                arguments += $" -p:UserName={Quote(profile.UserName)}";
                displayArguments += $" -p:UserName={Quote(profile.UserName)}";
            }

            var password = credentials.ResolvePassword(project, profile);
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "Web Deploy 401: no password. VS stores it encrypted in the .pubxml.user; Mozart could not decrypt it. Paste the deploy password on the project Settings tab.");
            }

            arguments += $" -p:Password={Quote(password)}";
            displayArguments += $" -p:Password={PasswordMask}";
        }

        arguments += Extra(project);
        displayArguments += Extra(project);

        var versionLabel = bump is null ? string.Empty : $" · v{bump.NewVersion}";
        var job = jobs.Enqueue(new Job
        {
            ProjectId = project.Id,
            ProjectName = project.DisplayName,
            Kind = JobKind.Publish,
            Title = $"Deploy → {profile.Name} ({profile.MethodLabel}){versionLabel}",
            FileName = _options.DotnetPath,
            Arguments = arguments,
            DisplayArguments = displayArguments,
            WorkingDirectory = project.WorkingDirectory,
            RequiredLogHints = profile.Method == PublishMethod.MsDeploy
                ? ["msdeploy", "sitemanifest", "Updating file", "Web App was published successfully", "Publish Succeeded"]
                : [],
            LaunchUrl = FirstUrl(profile.SiteUrlToLaunchAfterPublish, project.HealthUrl),
        });

        if (bump is not null)
        {
            job.Append(LogSeverity.System, $"Version {bump.OldVersion} → {bump.NewVersion} ({bump.FilePath})");
        }

        project.LastDeployUtc = DateTimeOffset.UtcNow;
        await store.SaveAsync(project);
        return job;
    }

    /// <summary>
    /// Queues Web Deploy publish jobs for every enabled project that has a usable MSDeploy profile.
    /// Order: Storage → Identity → OAuth clients (see <see cref="DeployAllPriority"/>).
    /// </summary>
    public async Task<DeployAllResult> DeployAllAsync(IReadOnlyList<CicdProject> projects)
    {
        var jobs = new List<Job>();
        var skipped = new List<string>();

        foreach (var project in projects
                     .Where(p => p.IsEnabled)
                     .OrderBy(p => DeployAllPriority(p.DisplayName))
                     .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var profile = publishProfiles.ResolveDeployProfile(project);
            if (profile is null)
            {
                skipped.Add($"{project.DisplayName}: no publish profile");
                continue;
            }

            if (profile.Method != PublishMethod.MsDeploy)
            {
                skipped.Add($"{project.DisplayName}: no Web Deploy profile (only {profile.MethodLabel})");
                continue;
            }

            try
            {
                jobs.Add(await PublishAsync(project, profile.Name));
            }
            catch (Exception ex)
            {
                skipped.Add($"{project.DisplayName}: {ex.Message}");
                errorLog.Write("DeployAll", $"Could not queue deploy for {project.DisplayName}", ex);
            }
        }

        return new DeployAllResult(jobs, skipped);
    }

    private static int DeployAllPriority(string displayName)
    {
        var index = Array.FindIndex(
            DeployAllOrder,
            name => string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? DeployAllOrder.Length : index;
    }

  private static readonly string[] DeployAllOrder =
    [
        "Ampersand.Storage",
        "Ampersand.Identity",
        "Ampersand.FamilyJourney",
        "Ampersand.DiscordBot",
        "Ampersand.AI",
        "Ampersand.BotManager",
        "OneRing.Ampersand.EmailService",
        "Ampersand.Pegasus",
        "Ampersand.TerraRPG",
        "Ampersand.Products.Documentation.Wizard",
        "Ampersand.IT.Mvc",
    ];

    private Job Enqueue(
        CicdProject project,
        JobKind kind,
        string title,
        string fileName,
        string arguments,
        string? displayArguments = null) =>
        jobs.Enqueue(new Job
        {
            ProjectId = project.Id,
            ProjectName = project.DisplayName,
            Kind = kind,
            Title = title,
            FileName = fileName,
            Arguments = arguments,
            DisplayArguments = displayArguments ?? arguments,
            WorkingDirectory = project.WorkingDirectory,
        });

    private string Configuration(CicdProject project) =>
        string.IsNullOrWhiteSpace(project.Configuration) ? _options.DefaultConfiguration : project.Configuration;

    private static string Extra(CicdProject project) =>
        string.IsNullOrWhiteSpace(project.ExtraArguments) ? string.Empty : " " + project.ExtraArguments.Trim();

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    private static string? FirstUrl(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
