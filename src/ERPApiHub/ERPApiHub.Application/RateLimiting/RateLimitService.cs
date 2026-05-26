using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERPApiHub.Application.RateLimiting;

/// <summary>
/// Redis-based rate limiting with fixed-window counter + token bucket burst.
/// FRD refs: FR-RLM-001 (tiers), FR-RLM-002 (per-endpoint), FR-RLM-003 (burst).
/// </summary>
public sealed class RateLimitService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitService> _logger;

    public RateLimitService(
        IConnectionMultiplexer redis,
        IOptions<RateLimitOptions> options,
        ILogger<RateLimitService> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Check if a request is within rate limit and return limit info.
    /// </summary>
    /// <param name="systemId">External system identifier (from JWT or API key)</param>
    /// <param name="tier">Rate limit tier</param>
    /// <param name="endpointType">Type of endpoint being accessed</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Rate limit result with remaining count and reset time</returns>
    public async Task<RateLimitResult> CheckAsync(
        string systemId,
        RateLimitTier tier,
        EndpointType endpointType,
        CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return RateLimitResult.Allowed(tier, endpointType, int.MaxValue, int.MaxValue, 0);
        }

        var db = _redis.GetDatabase();
        var effectiveLimit = _options.GetEffectiveLimit(tier, endpointType);
        var windowKey = $"erphub:ratelimit:{systemId}:{endpointType.ToString().ToLower()}";
        var window = TimeSpan.FromSeconds(_options.WindowSeconds);

        try
        {
            // Fixed-window counter with atomic increment
            var count = await db.StringIncrementAsync(windowKey);

            // Set TTL on first request in window
            if (count == 1)
            {
                await db.KeyExpireAsync(windowKey, window);
            }

            // Get remaining TTL for reset time
            var ttl = await db.KeyTimeToLiveAsync(windowKey);
            var resetInSeconds = ttl?.TotalSeconds ?? _options.WindowSeconds;

            var remaining = Math.Max(0, effectiveLimit - (int)count);
            var isAllowed = count <= effectiveLimit;

            if (!isAllowed)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for system {SystemId} tier {Tier} endpoint {Endpoint}. " +
                    "Count: {Count}/{Limit}, Reset in {ResetSeconds}s",
                    systemId, tier, endpointType, count, effectiveLimit, resetInSeconds);
            }

            return new RateLimitResult
            {
                IsAllowed = isAllowed,
                Tier = tier,
                EndpointType = endpointType,
                Limit = effectiveLimit,
                Remaining = remaining,
                ResetInSeconds = (int)resetInSeconds
            };
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed during rate limit check. Allowing request (fail-open).");
            // Fail-open: if Redis is down, allow the request
            return RateLimitResult.Allowed(tier, endpointType, effectiveLimit, effectiveLimit, 0);
        }
    }

    /// <summary>
    /// Check burst capacity using token bucket algorithm.
    /// </summary>
    public async Task<bool> CheckBurstAsync(
        string systemId,
        RateLimitTier tier,
        CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return true;
        }

        var db = _redis.GetDatabase();
        var burstCapacity = _options.GetBurstCapacity(tier);
        var burstKey = $"erphub:ratelimit:burst:{systemId}";

        try
        {
            var current = await db.StringGetAsync(burstKey);
            long currentCount = current.HasValue ? (long)current : 0;

            if (currentCount >= burstCapacity)
            {
                _logger.LogWarning(
                    "Burst limit exceeded for system {SystemId}. Burst: {Current}/{Capacity}",
                    systemId, currentCount, burstCapacity);
                return false;
            }

            // Increment burst counter
            var newCount = await db.StringIncrementAsync(burstKey);

            // Set TTL to window if first burst request
            if (newCount == 1)
            {
                await db.KeyExpireAsync(burstKey, TimeSpan.FromSeconds(_options.WindowSeconds));
            }

            return newCount <= burstCapacity;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed during burst check. Allowing request (fail-open).");
            return true;
        }
    }

    /// <summary>
    /// Resolve tier from system_id by looking up external_systems table.
    /// Falls back to default tier for unknown systems.
    /// </summary>
    public RateLimitTier ResolveTier(string? rateLimitTierStr)
    {
        if (string.IsNullOrWhiteSpace(rateLimitTierStr))
        {
            return _options.DefaultTier;
        }

        return Enum.TryParse<RateLimitTier>(rateLimitTierStr, out var tier)
            ? tier
            : _options.DefaultTier;
    }
}

/// <summary>
/// Result of a rate limit check.
/// </summary>
public sealed class RateLimitResult
{
    public bool IsAllowed { get; init; }
    public RateLimitTier Tier { get; init; }
    public EndpointType EndpointType { get; init; }
    public int Limit { get; init; }
    public int Remaining { get; init; }
    public int ResetInSeconds { get; init; }

    public static RateLimitResult Allowed(
        RateLimitTier tier,
        EndpointType endpointType,
        int limit,
        int remaining,
        int resetInSeconds) => new()
    {
        IsAllowed = true,
        Tier = tier,
        EndpointType = endpointType,
        Limit = limit,
        Remaining = remaining,
        ResetInSeconds = resetInSeconds
    };
}