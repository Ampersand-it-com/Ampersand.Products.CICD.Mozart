# Mozart — local CI/CD panel

Blazor Server UI for managing .NET projects on this machine: pick solutions from disk, run restore / build / test, create and edit publish profiles, deploy, watch live logs, and ping health URLs.

## Run

```powershell
dotnet run --project src/Ampersand.Mozart.WEB --launch-profile http
```

Opens `http://localhost:5258`.

## What it does

- **Add projects** — Windows file dialog (`.sln` / `.csproj`, multi-select).
- **Dashboard** — cards with git branch, last job, health/ping, one-click actions. **Deploy all** queues Web Deploy for every enabled project (Storage → Identity → …). **Commit all** queues `git add -A` → commit → push for repos with local changes or unpushed commits.
- **Project page**
  - Info: SDK, TFM, sources, git (branch / dirty / ahead-behind), last deploy.
  - Publish profiles: create Folder or Web Deploy `.pubxml` under `Properties/PublishProfiles`, edit fields or raw XML, set default, deploy.
  - Jobs: history for that project.
  - Settings: health URL, ping host, extra `dotnet` args, test path.
- **Deploy** bumps `[assembly: AssemblyVersion]` / `AssemblyFileVersion` in `Program.cs` (Ampersand style, last number +1). Syncs csproj `Version` tags if they exist.
- **Job logs** — every command is appended to `src/Ampersand.Mozart.WEB/data/jobs/{id}.log` as it runs (survives restart). Also `/api/jobs/{id}/log`.
- **Error log** — all `LogError` + failed jobs + deploy skips go to `data/logs/mozart-errors.log`. Open in browser: `/api/logs/errors` (or `?tail=200` for last lines). Share this file with the agent for triage.

## Deploy

- **Folder / Web Deploy** → `dotnet publish` with the full `.pubxml` path. Empty profiles are skipped. Web Deploy that only builds locally is marked failed. Password: Mozart secrets or the existing `.pubxml.user`.
- **Deploy all** (dashboard button) — same as `scripts/deploy-all.ps1`: MSDeploy profiles only, priority order Storage → Identity → clients. Progress on **Jobs**.
- **Commit all** / per-project **commit+push** — `git add -A`, auto message `Mozart: {project} @ {yyyy-MM-dd HH:mm}`, then `git push`. Skips clean repos with nothing to push.

## Config

`src/Ampersand.Mozart.WEB/appsettings.json` → `Mozart`:

| Key | Meaning |
|---|---|
| `DataDirectory` | Where `projects.json` is stored |
| `ScanRoots` | Extra start folders in the picker |
| `ScanMaxDepth` | How deep "Scan this folder" walks |
| `MaxParallelJobs` | Concurrent commands |
| `JobTimeoutMinutes` | Kill a hung command |
| `HealthPollSeconds` | Background ping interval (`0` = off) |

This panel runs commands on the host. Keep it on localhost.
