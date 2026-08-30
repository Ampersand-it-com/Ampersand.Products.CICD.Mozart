namespace Ampersand.Mozart.WEB.Models;

public sealed record DeployAllResult(IReadOnlyList<Job> Jobs, IReadOnlyList<string> Skipped);
