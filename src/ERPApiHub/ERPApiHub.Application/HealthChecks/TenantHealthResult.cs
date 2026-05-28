namespace ERPApiHub.Application.HealthChecks;

public sealed record TenantHealthResult
{
    public string TenantId { get; init; } = string.Empty;
    public string Status { get; init; } = TenantHealthStatuses.Unhealthy;
    public TimeSpan? ResponseTime { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
}

public static class TenantHealthStatuses
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Unhealthy = "unhealthy";
}
