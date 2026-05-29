using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERPApiHub.Application.RateLimiting;

/// <summary>
/// Redis sliding-window rate limiter backed by one atomic Lua script.
/// </summary>
public class RedisRateLimiter : IRateLimiter
{
    private const string SlidingWindowScript = """
        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, ARGV[3])
        local count = redis.call('ZCARD', KEYS[1])
        redis.call('EXPIRE', KEYS[1], ARGV[4])
        return count
    """;

    private readonly IConnectionMultiplexer _redis;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RedisRateLimiter> _logger;
    private byte[]? _slidingWindowSha;

    public RedisRateLimiter(
        IConnectionMultiplexer redis,
        IOptions<RateLimitOptions> options,
        ILogger<RedisRateLimiter> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

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

        ct.ThrowIfCancellationRequested();

        var effectiveLimit = _options.GetEffectiveLimit(tier, endpointType);
        var windowMs = Math.Max(1, _options.WindowSeconds) * 1000L;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cutoffMs = nowMs - windowMs;
        var ttlSeconds = Math.Max(2, _options.WindowSeconds * 2);
        var windowKey = $"erphub:ratelimit:{systemId}:{endpointType.ToString().ToLowerInvariant()}";
        var member = $"{nowMs}:{Guid.NewGuid():N}";

        try
        {
            var count = (long)await EvaluateSlidingWindowAsync(
                windowKey,
                nowMs,
                cutoffMs,
                member,
                ttlSeconds);

            var remaining = Math.Max(0, effectiveLimit - (int)count);
            var isAllowed = count <= effectiveLimit;

            if (!isAllowed)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for system {SystemId} tier {Tier} endpoint {Endpoint}. Count: {Count}/{Limit}",
                    systemId,
                    tier,
                    endpointType,
                    count,
                    effectiveLimit);
            }

            return new RateLimitResult
            {
                IsAllowed = isAllowed,
                Tier = tier,
                EndpointType = endpointType,
                Limit = effectiveLimit,
                Remaining = remaining,
                ResetInSeconds = _options.WindowSeconds
            };
        }
        catch (RedisException ex)
        {
            _logger.LogError(
                ex,
                "Redis failed during atomic rate limit check for system {SystemId}. Allowing request (fail-open).",
                systemId);
            return RateLimitResult.Allowed(tier, endpointType, effectiveLimit, effectiveLimit, 0);
        }
    }

    public Task<bool> CheckBurstAsync(
        string systemId,
        RateLimitTier tier,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

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

    private async Task<RedisResult> EvaluateSlidingWindowAsync(
        RedisKey key,
        long nowMs,
        long cutoffMs,
        RedisValue member,
        int ttlSeconds)
    {
        var db = _redis.GetDatabase();
        var sha = await GetSlidingWindowShaAsync();
        var keys = new RedisKey[] { key };
        var values = new RedisValue[] { nowMs, member, cutoffMs, ttlSeconds };

        try
        {
            return await db.ScriptEvaluateAsync(sha, keys, values);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOSCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Redis rate limit Lua script was evicted; falling back to EVAL and reloading SHA.");
            _slidingWindowSha = null;
            var result = await db.ScriptEvaluateAsync(SlidingWindowScript, keys, values);
            _slidingWindowSha = await LoadScriptAsync();
            return result;
        }
    }

    private async Task<byte[]> GetSlidingWindowShaAsync()
    {
        if (_slidingWindowSha is not null)
        {
            return _slidingWindowSha;
        }

        _slidingWindowSha = await LoadScriptAsync();
        return _slidingWindowSha;
    }

    private async Task<byte[]> LoadScriptAsync()
    {
        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (server.IsConnected)
            {
                return await server.ScriptLoadAsync(SlidingWindowScript);
            }
        }

        throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connected Redis server is available for script load.");
    }
}
