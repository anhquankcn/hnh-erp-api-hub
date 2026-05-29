using System.Globalization;
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

    private const string BurstTokenBucketScript = """
        local capacity = tonumber(ARGV[1])
        local refill_per_ms = tonumber(ARGV[2])
        local now_ms = tonumber(ARGV[3])
        local ttl_seconds = tonumber(ARGV[4])

        local bucket = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(bucket[1])
        local ts = tonumber(bucket[2])

        if tokens == nil then
            tokens = capacity
        end

        if ts == nil then
            ts = now_ms
        end

        local elapsed = math.max(0, now_ms - ts)
        tokens = math.min(capacity, tokens + (elapsed * refill_per_ms))

        local allowed = 0
        if tokens >= 1 then
            tokens = tokens - 1
            allowed = 1
        end

        redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now_ms)
        redis.call('EXPIRE', KEYS[1], ttl_seconds)
        return allowed
    """;

    private readonly IConnectionMultiplexer _redis;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RedisRateLimiter> _logger;
    private byte[]? _slidingWindowSha;
    private byte[]? _burstTokenBucketSha;

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

    public async Task<bool> CheckBurstAsync(
        string systemId,
        RateLimitTier tier,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return true;
        }

        var capacity = Math.Max(1, _options.GetBurstCapacity(tier));
        var refillPerMs = Math.Max(1, GetTierRequestsPerMinute(tier)) / 60_000d;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ttlSeconds = Math.Max(2, (int)Math.Ceiling(capacity / refillPerMs / 1000d) * 2);
        var key = $"erphub:ratelimit:burst:{systemId}";

        try
        {
            var allowed = (long)await EvaluateBurstTokenBucketAsync(key, capacity, refillPerMs, nowMs, ttlSeconds);
            if (allowed == 0)
            {
                _logger.LogWarning(
                    "Burst limit exceeded for system {SystemId} tier {Tier}. Capacity: {Capacity}",
                    systemId,
                    tier,
                    capacity);
            }

            return allowed == 1;
        }
        catch (RedisException ex)
        {
            _logger.LogError(
                ex,
                "Redis failed during atomic burst rate limit check for system {SystemId}. Allowing request (fail-open).",
                systemId);
            return true;
        }
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
            _slidingWindowSha = await LoadScriptAsync(SlidingWindowScript);
            return result;
        }
    }

    private async Task<RedisResult> EvaluateBurstTokenBucketAsync(
        RedisKey key,
        int capacity,
        double refillPerMs,
        long nowMs,
        int ttlSeconds)
    {
        var db = _redis.GetDatabase();
        var sha = await GetBurstTokenBucketShaAsync();
        var keys = new RedisKey[] { key };
        var values = new RedisValue[] { capacity, refillPerMs.ToString("R", CultureInfo.InvariantCulture), nowMs, ttlSeconds };

        try
        {
            return await db.ScriptEvaluateAsync(sha, keys, values);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOSCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Redis burst limit Lua script was evicted; falling back to EVAL and reloading SHA.");
            _burstTokenBucketSha = null;
            var result = await db.ScriptEvaluateAsync(BurstTokenBucketScript, keys, values);
            _burstTokenBucketSha = await LoadScriptAsync(BurstTokenBucketScript);
            return result;
        }
    }

    private async Task<byte[]> GetSlidingWindowShaAsync()
    {
        if (_slidingWindowSha is not null)
        {
            return _slidingWindowSha;
        }

        _slidingWindowSha = await LoadScriptAsync(SlidingWindowScript);
        return _slidingWindowSha;
    }

    private async Task<byte[]> GetBurstTokenBucketShaAsync()
    {
        if (_burstTokenBucketSha is not null)
        {
            return _burstTokenBucketSha;
        }

        _burstTokenBucketSha = await LoadScriptAsync(BurstTokenBucketScript);
        return _burstTokenBucketSha;
    }

    private int GetTierRequestsPerMinute(RateLimitTier tier)
    {
        var tierKey = tier.ToString();
        if (!_options.Tiers.TryGetValue(tierKey, out var tierConfig))
        {
            tierConfig = _options.Tiers["TIER_3"];
        }

        return tierConfig.RequestsPerMinute;
    }

    private async Task<byte[]> LoadScriptAsync(string script)
    {
        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (server.IsConnected)
            {
                return await server.ScriptLoadAsync(script);
            }
        }

        throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connected Redis server is available for script load.");
    }
}
