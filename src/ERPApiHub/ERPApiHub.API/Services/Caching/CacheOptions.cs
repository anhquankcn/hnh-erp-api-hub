namespace ERPApiHub.API.Services.Caching;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public bool Enabled { get; set; } = true;

    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan L1Ttl { get; set; } = TimeSpan.FromMinutes(1);

    public string RedisKeyPrefix { get; set; } = "erphub:";

    public string TagKeyPrefix { get; set; } = "cache:tag:";

    public string KeyIndexSet { get; set; } = "cache:keys";
}
