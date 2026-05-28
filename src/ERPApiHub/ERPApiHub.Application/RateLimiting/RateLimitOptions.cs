namespace ERPApiHub.Application.RateLimiting;

/// <summary>
/// Rate limiting configuration per FRD FR-RLM-001/002/003.
/// </summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Whether rate limiting is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Window size in seconds for fixed-window counter.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Default tier for unregistered systems.</summary>
    public RateLimitTier DefaultTier { get; set; } = RateLimitTier.TIER_3;

    /// <summary>Tier configurations.</summary>
    public Dictionary<string, TierConfig> Tiers { get; set; } = new()
    {
        ["TIER_1"] = new() { RequestsPerMinute = 10_000, BurstMultiplier = 2 },
        ["TIER_2"] = new() { RequestsPerMinute = 1_000, BurstMultiplier = 2 },
        ["TIER_3"] = new() { RequestsPerMinute = 100, BurstMultiplier = 2 }
    };

    /// <summary>Per-endpoint rate limit percentages of tier limit.</summary>
    public Dictionary<string, double> EndpointReduction { get; set; } = new()
    {
        ["Ingestion"] = 0.50,
        ["Query"] = 1.00,
        ["WebhookManagement"] = 0.10,
        ["Admin"] = 0.05,
        ["Other"] = 1.00
    };

    /// <summary>Get effective limit for a tier and endpoint type.</summary>
    public int GetEffectiveLimit(RateLimitTier tier, EndpointType endpointType)
    {
        var tierKey = tier.ToString();
        if (!Tiers.TryGetValue(tierKey, out var tierConfig))
        {
            tierConfig = Tiers["TIER_3"];
        }

        var endpointKey = endpointType.ToString();
        if (!EndpointReduction.TryGetValue(endpointKey, out var reduction))
        {
            reduction = 1.0;
        }

        return (int)(tierConfig.RequestsPerMinute * reduction);
    }

    /// <summary>Get burst capacity for a tier.</summary>
    public int GetBurstCapacity(RateLimitTier tier)
    {
        var tierKey = tier.ToString();
        if (!Tiers.TryGetValue(tierKey, out var tierConfig))
        {
            tierConfig = Tiers["TIER_3"];
        }

        return (int)(tierConfig.RequestsPerMinute * tierConfig.BurstMultiplier);
    }
}

public class TierConfig
{
    /// <summary>Requests per minute sustained rate.</summary>
    public int RequestsPerMinute { get; set; }

    /// <summary>Burst multiplier over sustained rate (e.g., 2x).</summary>
    public double BurstMultiplier { get; set; } = 2.0;
}