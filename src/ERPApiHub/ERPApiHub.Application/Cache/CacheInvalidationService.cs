using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ERPApiHub.Application.Cache;

public sealed class CacheInvalidationService
{
    private const string RedisPrefix = "erphub:";
    private const string TagKeyPrefix = "cache:tag:";
    private const string KeyIndexSet = "cache:keys";

    private readonly ICacheService _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<CacheInvalidationService> _logger;

    public CacheInvalidationService(
        ICacheService cache,
        IConnectionMultiplexer redis,
        ILogger<CacheInvalidationService> logger)
    {
        _cache = cache;
        _redis = redis;
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public async Task RegisterQueryKeyAsync(
        string tenantId,
        string doctype,
        string cacheKey,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(cacheKey).ToString();
        var tags = new[]
        {
            "query",
            $"tenant:{tenantId}",
            $"doctype:{doctype}",
            $"query:{tenantId}:{doctype}"
        };

        await _database.SetAddAsync(BuildRedisKey(KeyIndexSet), redisKey);
        foreach (var tag in tags.Select(NormalizeTag))
        {
            var tagKey = BuildTagKey(tag);
            await _database.SetAddAsync(tagKey, redisKey);
            await _database.KeyExpireAsync(tagKey, ttl);
        }
    }

    public async Task<CacheInvalidationResult> InvalidateDoctypeAsync(
        string doctype,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var tag = string.IsNullOrWhiteSpace(tenantId)
            ? $"doctype:{doctype}"
            : $"query:{tenantId}:{doctype}";

        var removed = await InvalidateTagAsync(tag, cancellationToken);
        if (removed.RemovedCount == 0)
        {
            var pattern = string.IsNullOrWhiteSpace(tenantId)
                ? $"query:*:{doctype}:*"
                : $"query:{tenantId}:{doctype}:*";

            return await InvalidatePatternAsync(pattern, cancellationToken);
        }

        return new CacheInvalidationResult("doctype", tag, removed.RemovedCount);
    }

    public async Task<CacheInvalidationResult> InvalidateTagAsync(
        string tag,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedTag = NormalizeTag(tag);
        var tagKey = BuildTagKey(normalizedTag);
        var redisKeys = (await _database.SetMembersAsync(tagKey))
            .Where(value => value.HasValue)
            .Select(value => value.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var removed = await RemoveKeysAsync(redisKeys, cancellationToken);
        await _database.KeyDeleteAsync(tagKey);

        _logger.LogInformation(
            "Invalidated {Count} cache keys for tag {Tag}.",
            removed,
            normalizedTag);

        return new CacheInvalidationResult("tag", normalizedTag, removed);
    }

    public async Task<CacheInvalidationResult> InvalidatePatternAsync(
        string pattern,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisPattern = BuildRedisKey(pattern).ToString();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            await foreach (var key in server.KeysAsync(pattern: redisPattern).WithCancellation(cancellationToken))
            {
                keys.Add(key.ToString());
            }
        }

        var removed = await RemoveKeysAsync(keys, cancellationToken);
        return new CacheInvalidationResult("pattern", pattern, removed);
    }

    private async Task<int> RemoveKeysAsync(
        IEnumerable<string> redisKeys,
        CancellationToken cancellationToken)
    {
        var removed = 0;

        foreach (var redisKey in redisKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var logicalKey = ToLogicalKey(redisKey);
            await _cache.RemoveAsync(logicalKey, cancellationToken);
            await _database.SetRemoveAsync(BuildRedisKey(KeyIndexSet), redisKey);
            removed++;
        }

        return removed;
    }

    private static RedisKey BuildTagKey(string tag) => BuildRedisKey($"{TagKeyPrefix}{tag}");

    private static RedisKey BuildRedisKey(string key)
    {
        if (key.StartsWith(RedisPrefix, StringComparison.Ordinal))
        {
            return key;
        }

        return $"{RedisPrefix}{key}";
    }

    private static string ToLogicalKey(string redisKey) =>
        redisKey.StartsWith(RedisPrefix, StringComparison.Ordinal)
            ? redisKey[RedisPrefix.Length..]
            : redisKey;

    private static string NormalizeTag(string tag) => tag.Trim().ToLowerInvariant();
}

public sealed record CacheInvalidationResult(string Type, string Target, int RemovedCount);
