namespace ERPApiHub.Application.HealthChecks;

public sealed class TenantHealthCheckOptions
{
    public const string SectionName = "TenantHealthCheck";

    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 10;
    public int DegradedThresholdMs { get; set; } = 2000;
    public string[] AlertOn { get; set; } = [TenantHealthStatuses.Unhealthy, TenantHealthStatuses.Degraded];
    public int MaxDegreeOfParallelism { get; set; } = 5;

    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes > 0 ? IntervalMinutes : 5);
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds > 0 ? TimeoutSeconds : 10);
    public TimeSpan DegradedThreshold => TimeSpan.FromMilliseconds(DegradedThresholdMs > 0 ? DegradedThresholdMs : 2000);
}
