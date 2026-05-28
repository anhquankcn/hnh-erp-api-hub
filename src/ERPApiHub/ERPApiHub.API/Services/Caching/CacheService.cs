using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERPApiHub.API.Services.Caching;

public sealed class CacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, KeyedSemaphore> KeyLocks = new();

    private readonly IMemoryCache _memoryCache;
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly CacheOptions _cacheOptions;
    private readonly RedisOptions _redisOptions;
    private readonly ILogger<CacheService> _logger;
    private readonly ConcurrentDictionary<string, byte> _l1Keys = new();
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<string>> _l1Tags = new();

    public CacheService(
        IMemoryCache memoryCache,
        IConnectionMultiplexer connectionMultiplexer,
        IOptions<CacheOptions> cacheOptions,
        IOptions<RedisOptions> redisOptions,
        ILogger<CacheService> logger)
    {
        _memoryCache = memoryCache;
        _connectionMultiplexer = connectionMultiplexer;
        _database = connectionMultiplexer.GetDatabase();
        _cacheOptions = cacheOptions.Value;
        _redisOptions = redisOptions.Value;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await GetAsync<T>(key, CacheLevel.All, cancellationToken);
        return value.Found ? value.Value : default;
    }

    public async Task<CacheResult<T>> GetAsync<T>(
        string key,
        CacheLevel level,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_cacheOptions.Enabled)
        {
            return CacheResult<T>.Miss();
        }

        if (level is CacheLevel.L1 or CacheLevel.All)
        {
            if (_memoryCache.TryGetValue<T>(key, out var l1Value))
            {
                return CacheResult<T>.Hit(l1Value);
            }
        }

        if (level is CacheLevel.L2 or CacheLevel.All)
        {
            var redisValue = await _database.StringGetAsync(BuildRedisKey(key));
            if (!redisValue.IsNullOrEmpty)
            {
                var value = JsonSerializer.Deserialize<T>(redisValue!, SerializerOptions);
                if (level is CacheLevel.All && value is not null)
                {
                    SetL1(key, value, _cacheOptions.L1Ttl, InferTags(key));
                }

                return CacheResult<T>.Hit(value);
            }
        }

        return CacheResult<T>.Miss();
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        await SetAsync(key, value, expiration, CacheLevel.All, null, cancellationToken);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration,
        CacheLevel level,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_cacheOptions.Enabled)
        {
            return;
        }

        var ttl = expiration ?? _cacheOptions.DefaultTtl;
        var normalizedTags = NormalizeTags(tags ?? InferTags(key));

        if (level is CacheLevel.L1 or CacheLevel.All)
        {
            SetL1(key, value, ttl < _cacheOptions.L1Ttl ? ttl : _cacheOptions.L1Ttl, normalizedTags);
        }

        if (level is CacheLevel.L2 or CacheLevel.All)
        {
            var serializedValue = JsonSerializer.Serialize(value, SerializerOptions);
            var redisKey = BuildRedisKey(key);
            await _database.StringSetAsync(redisKey, serializedValue, ttl);
            await _database.SetAddAsync(BuildKeyIndexSet(), redisKey.ToString());

            foreach (var tag in normalizedTags)
            {
                await _database.SetAddAsync(BuildTagKey(tag), redisKey.ToString());
            }
        }
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, CacheLevel.All, cancellationToken);
        if (cached.Found)
        {
            return cached.Value!;
        }

        var keyLock = await RentKeyLockAsync(key);
        var lockAcquired = false;
        try
        {
            await keyLock.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;

            cached = await GetAsync<T>(key, CacheLevel.All, cancellationToken);
            if (cached.Found)
            {
                return cached.Value!;
            }

            var created = await factory(cancellationToken);
            await SetAsync(key, created, expiration, CacheLevel.All, tags, cancellationToken);
            return created;
        }
        finally
        {
            if (lockAcquired)
            {
                keyLock.Semaphore.Release();
            }

            if (keyLock.ReleaseWaiterAndTryRetire())
            {
                KeyLocks.TryRemove(new KeyValuePair<string, KeyedSemaphore>(key, keyLock));
            }
        }
    }

    private static async Task<KeyedSemaphore> RentKeyLockAsync(string key)
    {
        while (true)
        {
            var keyLock = KeyLocks.GetOrAdd(key, _ => new KeyedSemaphore());
            if (keyLock.TryAddWaiter())
            {
                return keyLock;
            }

            await Task.Yield();
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _memoryCache.TryGetValue(key, out _)
            || await _database.KeyExistsAsync(BuildRedisKey(key));
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await RemoveAsync(key, CacheLevel.All, cancellationToken);
    }

    public async Task RemoveAsync(
        string key,
        CacheLevel level,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (level is CacheLevel.L1 or CacheLevel.All)
        {
            RemoveL1(key);
        }

        if (level is CacheLevel.L2 or CacheLevel.All)
        {
            var redisKey = BuildRedisKey(key);
            await _database.KeyDeleteAsync(redisKey);
            await RemoveFromIndexesAsync([redisKey], cancellationToken);
        }
    }

    public async Task<int> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var removed = RemoveL1ByPattern(pattern);
        var redisKeys = await FindRedisKeysAsync(pattern, cancellationToken);
        if (redisKeys.Count > 0)
        {
            removed += (int)await _database.KeyDeleteAsync(redisKeys.ToArray());
            await RemoveFromIndexesAsync(redisKeys, cancellationToken);
        }

        return removed;
    }

    public async Task<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisTagKey = BuildTagKey(NormalizeTag(tag));
        var redisKeys = (await _database.SetMembersAsync(redisTagKey))
            .Where(value => value.HasValue)
            .Select(value => (RedisKey)value.ToString())
            .ToArray();

        var removed = RemoveL1ByTag(tag);
        if (redisKeys.Length > 0)
        {
            removed += (int)await _database.KeyDeleteAsync(redisKeys);
            await RemoveFromIndexesAsync(redisKeys, cancellationToken);
        }

        await _database.KeyDeleteAsync(redisTagKey);
        return removed;
    }

    public async Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await _database.StringIncrementAsync(BuildRedisKey(key));
        _memoryCache.Remove(key);
        return value;
    }

    public async Task ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.KeyExpireAsync(BuildRedisKey(key), expiration);
        _memoryCache.Remove(key);
    }

    private void SetL1<T>(string key, T value, TimeSpan ttl, IReadOnlyCollection<string> tags)
    {
        _memoryCache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        }.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            if (evictedKey is string cacheKey)
            {
                _l1Keys.TryRemove(cacheKey, out _);
                _l1Tags.TryRemove(cacheKey, out _);
            }
        }));

        _l1Keys.TryAdd(key, 0);
        _l1Tags[key] = tags;
    }

    private void RemoveL1(string key)
    {
        _memoryCache.Remove(key);
        _l1Keys.TryRemove(key, out _);
        _l1Tags.TryRemove(key, out _);
    }

    private int RemoveL1ByPattern(string pattern)
    {
        var regex = WildcardToRegex(pattern);
        var keys = _l1Keys.Keys.Where(key => regex.IsMatch(key)).ToArray();

        foreach (var key in keys)
        {
            RemoveL1(key);
        }

        return keys.Length;
    }

    private int RemoveL1ByTag(string tag)
    {
        var normalizedTag = NormalizeTag(tag);
        var keys = _l1Keys.Keys
            .Where(key => L1KeyHasTag(key, normalizedTag))
            .ToArray();

        foreach (var key in keys)
        {
            RemoveL1(key);
        }

        return keys.Length;
    }

    private async Task<IReadOnlyCollection<RedisKey>> FindRedisKeysAsync(
        string pattern,
        CancellationToken cancellationToken)
    {
        var redisPattern = BuildRedisKey(pattern).ToString();
        var keys = new HashSet<RedisKey>();

        foreach (var endpoint in _connectionMultiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var server = _connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            await foreach (var key in server.KeysAsync(pattern: redisPattern).WithCancellation(cancellationToken))
            {
                keys.Add(key);
            }
        }

        if (keys.Count == 0)
        {
            var indexedKeys = await _database.SetMembersAsync(BuildKeyIndexSet());
            var regex = WildcardToRegex(redisPattern);
            foreach (var key in indexedKeys)
            {
                if (key.HasValue && regex.IsMatch(key.ToString()))
                {
                    keys.Add((RedisKey)key.ToString());
                }
            }
        }

        return keys;
    }

    private async Task RemoveFromIndexesAsync(IEnumerable<RedisKey> redisKeys, CancellationToken cancellationToken)
    {
        foreach (var redisKey in redisKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _database.SetRemoveAsync(BuildKeyIndexSet(), redisKey.ToString());
        }
    }

    private RedisKey BuildRedisKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        var prefix = !string.IsNullOrWhiteSpace(_cacheOptions.RedisKeyPrefix)
            ? _cacheOptions.RedisKeyPrefix
            : _redisOptions.InstanceName;

        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "erphub:";
        }

        if (!prefix.EndsWith(':'))
        {
            prefix += ':';
        }

        return key.StartsWith(prefix, StringComparison.Ordinal)
            ? key
            : $"{prefix}{key}";
    }

    private RedisKey BuildTagKey(string tag) => BuildRedisKey($"{_cacheOptions.TagKeyPrefix}{tag}");

    private RedisKey BuildKeyIndexSet() => BuildRedisKey(_cacheOptions.KeyIndexSet);

    private static IReadOnlyCollection<string> InferTags(string key)
    {
        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (parts.Length > 0)
        {
            tags.Add(parts[0]);
        }

        if (parts.Length >= 3 && parts[0].Equals("query", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add($"tenant:{parts[1]}");
            tags.Add($"doctype:{parts[2]}");
            tags.Add($"query:{parts[1]}:{parts[2]}");
        }

        return tags;
    }

    private static IReadOnlyCollection<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(NormalizeTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeTag(string tag) => tag.Trim().ToLowerInvariant();

    private bool L1KeyHasTag(string key, string normalizedTag)
    {
        if (_l1Tags.TryGetValue(key, out var tags)
            && tags.Any(tag => NormalizeTag(tag) == normalizedTag))
        {
            return true;
        }

        return InferTags(key).Any(tag => NormalizeTag(tag) == normalizedTag);
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed class KeyedSemaphore
    {
        private const int Retired = -1;
        private int _waiterCount;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddWaiter()
        {
            while (true)
            {
                var current = Volatile.Read(ref _waiterCount);
                if (current == Retired)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _waiterCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public bool ReleaseWaiterAndTryRetire()
        {
            var remaining = Interlocked.Decrement(ref _waiterCount);
            return remaining == 0
                && Interlocked.CompareExchange(ref _waiterCount, Retired, 0) == 0;
        }
    }
}

public readonly record struct CacheResult<T>(bool Found, T? Value)
{
    public static CacheResult<T> Hit(T? value) => new(true, value);

    public static CacheResult<T> Miss() => new(false, default);
}
