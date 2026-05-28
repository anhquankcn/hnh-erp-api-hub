namespace ERPApiHub.API.Services.Jobs;

public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    public bool Enabled { get; set; } = true;

    public bool DashboardEnabled { get; set; } = true;

    public string DashboardPath { get; set; } = "/hangfire";

    public string RedisPrefix { get; set; } = "erphub:hangfire:";

    public string CacheWarmingCron { get; set; } = "*/15 * * * *";

    public string CacheEvictionCron { get; set; } = "*/30 * * * *";

    public string HealthAggregationCron { get; set; } = "*/5 * * * *";

    public string[] TenantIds { get; set; } = [];

    public string[] FrequentlyAccessedDoctypes { get; set; } = [];

    public int WarmPageSize { get; set; } = 50;

    public int WarmPageCount { get; set; } = 1;

    public int HealthAggregationCacheTtlMinutes { get; set; } = 10;
}
