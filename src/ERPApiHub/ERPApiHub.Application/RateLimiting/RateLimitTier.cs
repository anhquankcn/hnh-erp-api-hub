namespace ERPApiHub.Application.RateLimiting;

/// <summary>
/// Rate limit tier definitions per FRD FR-RLM-001.
/// </summary>
public enum RateLimitTier
{
    /// <summary>TIER_1 Premium: 10,000 req/min</summary>
    TIER_1 = 1,

    /// <summary>TIER_2 Standard: 1,000 req/min</summary>
    TIER_2 = 2,

    /// <summary>TIER_3 Basic: 100 req/min</summary>
    TIER_3 = 3
}