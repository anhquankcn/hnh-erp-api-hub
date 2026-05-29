using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERPApiHub.Application.RateLimiting;

/// <summary>
/// Backward-compatible service name for existing callers.
/// </summary>
public sealed class RateLimitService : RedisRateLimiter
{
    public RateLimitService(
        IConnectionMultiplexer redis,
        IOptions<RateLimitOptions> options,
        ILogger<RedisRateLimiter> logger)
        : base(redis, options, logger)
    {
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
