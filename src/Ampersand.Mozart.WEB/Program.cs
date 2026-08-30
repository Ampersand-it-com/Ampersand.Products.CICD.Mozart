using Ampersand.Mozart.WEB.Components;
using Ampersand.Mozart.WEB.Services;
using Ampersand.Mozart.WEB.Services.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<MozartOptions>(builder.Configuration.GetSection(MozartOptions.SectionName));

builder.Services.AddSingleton<MozartErrorLog>();
builder.Services.AddSingleton<ILoggerProvider, MozartFileLoggerProvider>();

builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<HealthProbeService>();
builder.Services.AddSingleton<PublishProfileService>();
builder.Services.AddSingleton<ProjectInspector>();
builder.Services.AddSingleton<GitService>();
builder.Services.AddSingleton<GitCommitService>();
builder.Services.AddSingleton<NativeFilePicker>();
builder.Services.AddSingleton<VersionBumpService>();
builder.Services.AddSingleton<JobLogStore>();
builder.Services.AddSingleton<PublishCredentialService>();
builder.Services.AddSingleton<ProjectOperationsService>();

builder.Services.AddHostedService<JobWorker>();
builder.Services.AddHostedService<HealthMonitor>();

// Health probes target internal environments, so self-signed certificates must not fail the check.
builder.Services.AddHttpClient("health", (provider, client) =>
{
    var timeout = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MozartOptions>>().Value.HealthTimeoutSeconds;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(2, timeout));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozart-CICD-Panel/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = true,
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
});

var app = builder.Build();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is Exception ex)
    {
        app.Services.GetRequiredService<MozartErrorLog>()
            .Write("Unhandled", "AppDomain unhandled exception", ex);
    }
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    app.Services.GetRequiredService<MozartErrorLog>()
        .Write("Unhandled", "Unobserved task exception", e.Exception);
    e.SetObserved();
};

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/logs/errors", (MozartErrorLog errors, int? tail) =>
{
    var text = tail is > 0 ? errors.ReadTail(tail.Value) : errors.ReadAll();
    return text is null
        ? Results.Text($"No errors logged yet. File: {errors.FilePath}", "text/plain; charset=utf-8")
        : Results.Text(text, "text/plain; charset=utf-8");
});

app.MapGet("/api/jobs/{id}/log", (string id, JobManager jobs, JobLogStore logs) =>
{
    var fromDisk = logs.ReadText(id);
    if (fromDisk is not null)
    {
        return Results.Text(fromDisk, "text/plain; charset=utf-8");
    }

    var job = jobs.GetJob(id);
    return job is null
        ? Results.NotFound("Job log not found.")
        : Results.Text(
            $"Reason: {job.Diagnose()}{Environment.NewLine}Status: {job.Status}{Environment.NewLine}{Environment.NewLine}{job.PlainLog()}",
            "text/plain; charset=utf-8");
});

app.MapGet("/api/jobs/last", (JobLogStore logs) =>
{
    var last = logs.List(1).FirstOrDefault();
    if (last is null)
    {
        return Results.NotFound("No jobs yet.");
    }

    var text = logs.ReadText(last.Id);
    return text is null
        ? Results.NotFound("Log file missing.")
        : Results.Text(text, "text/plain; charset=utf-8");
});

app.Run();
