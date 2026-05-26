namespace ERPApiHub.Infrastructure.Caching;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";

    public string? Password { get; set; }

    public string InstanceName { get; set; } = "erphub:";

    public int DefaultTtlMinutes { get; set; } = 5;

    public int LookupTtlMinutes { get; set; } = 15;

    public TimeSpan DefaultTtl => TimeSpan.FromMinutes(DefaultTtlMinutes);

    public TimeSpan LookupTtl => TimeSpan.FromMinutes(LookupTtlMinutes);
}
