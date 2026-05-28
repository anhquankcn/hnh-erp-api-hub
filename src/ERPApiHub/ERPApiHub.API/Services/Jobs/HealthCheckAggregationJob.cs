using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ERPApiHub.API.Services.Jobs;

public sealed class HealthCheckAggregationJob(
    HealthCheckService healthCheckService,
    ICacheService cacheService,
    IOptions<JobOptions> options,
    ILogger<HealthCheckAggregationJob> logger)
{
    private const string CacheKey = "jobs:health:latest";

    public Task AggregateAsync() => AggregateAsync(CancellationToken.None);

    private async Task AggregateAsync(CancellationToken cancellationToken)
    {
        var jobOptions = options.Value;
        if (!jobOptions.Enabled)
        {
            return;
        }

        var report = await healthCheckService.CheckHealthAsync(_ => true, cancellationToken);
        var payload = new HealthCheckAggregationSnapshot
        {
            Status = report.Status.ToString(),
            TotalDurationMs = report.TotalDuration.TotalMilliseconds,
            CheckedAt = DateTimeOffset.UtcNow,
            Checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new HealthCheckAggregationEntry
                {
                    Status = entry.Value.Status.ToString(),
                    DurationMs = entry.Value.Duration.TotalMilliseconds,
                    Description = entry.Value.Description,
                    Error = entry.Value.Exception?.Message
                })
        };

        await cacheService.SetAsync(
            CacheKey,
            payload,
            TimeSpan.FromMinutes(Math.Max(1, jobOptions.HealthAggregationCacheTtlMinutes)),
            cancellationToken);

        logger.LogInformation("Aggregated health checks with status {Status}.", payload.Status);
    }
}

public sealed record HealthCheckAggregationSnapshot
{
    public string Status { get; init; } = string.Empty;

    public double TotalDurationMs { get; init; }

    public DateTimeOffset CheckedAt { get; init; }

    public IReadOnlyDictionary<string, HealthCheckAggregationEntry> Checks { get; init; } =
        new Dictionary<string, HealthCheckAggregationEntry>();
}

public sealed record HealthCheckAggregationEntry
{
    public string Status { get; init; } = string.Empty;

    public double DurationMs { get; init; }

    public string? Description { get; init; }

    public string? Error { get; init; }
}
