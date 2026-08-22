using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Ampersand.Mozart.WEB.Models;
using Microsoft.Extensions.Options;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// HTTP status + ICMP ping probes with an in-memory cache of the last result per project.
/// </summary>
public sealed class HealthProbeService(
    IHttpClientFactory httpClientFactory,
    IOptions<MozartOptions> options,
    ILogger<HealthProbeService> logger)
{
    private readonly ConcurrentDictionary<string, HealthResult> _cache = new();
    private readonly MozartOptions _options = options.Value;

    public event Action? Changed;

    public HealthResult? GetCached(string projectId) => _cache.GetValueOrDefault(projectId);

    public async Task<HealthResult> ProbeAsync(CicdProject project, CancellationToken cancellationToken = default)
    {
        var result = new HealthResult
        {
            Url = string.IsNullOrWhiteSpace(project.HealthUrl) ? null : project.HealthUrl.Trim(),
            PingHost = ResolvePingHost(project),
        };

        if (result.Url is not null)
        {
            await ProbeHttpAsync(result, cancellationToken);
        }

        if (result.PingHost is not null)
        {
            await ProbePingAsync(result, cancellationToken);
        }

        _cache[project.Id] = result;
        Changed?.Invoke();
        return result;
    }

    private async Task ProbeHttpAsync(HealthResult result, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("health");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, result.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stopwatch.Stop();
            result.HttpStatusCode = (int)response.StatusCode;
            result.HttpElapsedMs = stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.HttpElapsedMs = stopwatch.ElapsedMilliseconds;
            result.HttpError = ex.InnerException?.Message ?? ex.Message;
            logger.LogDebug(ex, "Health probe failed for {Url}", result.Url);
        }
    }

    private async Task ProbePingAsync(HealthResult result, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(
                result.PingHost!,
                TimeSpan.FromSeconds(Math.Max(1, _options.HealthTimeoutSeconds)),
                cancellationToken: cancellationToken);
            result.PingStatus = reply.Status.ToString();
            result.PingRoundtripMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception ex)
        {
            result.PingStatus = "Error";
            logger.LogDebug(ex, "Ping failed for {Host}", result.PingHost);
        }
    }

    private static string? ResolvePingHost(CicdProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.PingHost))
        {
            return project.PingHost.Trim();
        }

        if (!string.IsNullOrWhiteSpace(project.HealthUrl) &&
            Uri.TryCreate(project.HealthUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return null;
    }
}
