using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERPApiHub.Infrastructure.Caching;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);
}

public sealed class RedisCacheService : IRedisCacheService
{
    private const string RequiredKeyPrefix = "erphub:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database;
    private readonly RedisOptions _options;

    public RedisCacheService(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        _database = connectionMultiplexer.GetDatabase();
        _options = options.Value;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await _database.StringGetAsync(BuildKey(key));
        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value!, SerializerOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serializedValue = JsonSerializer.Serialize(value, SerializerOptions);
        await _database.StringSetAsync(BuildKey(key), serializedValue, ttl ?? _options.DefaultTtl);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.KeyDeleteAsync(BuildKey(key));
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        var cachedValue = await GetAsync<T>(key, cancellationToken);
        if (cachedValue is not null)
        {
            return cachedValue;
        }

        var createdValue = await factory(cancellationToken);
        await SetAsync(key, createdValue, ttl, cancellationToken);

        return createdValue;
    }

    private RedisKey BuildKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Redis cache key cannot be empty.", nameof(key));
        }

        if (key.StartsWith(RequiredKeyPrefix, StringComparison.Ordinal))
        {
            return key;
        }

        var prefix = string.IsNullOrWhiteSpace(_options.InstanceName)
            ? RequiredKeyPrefix
            : _options.InstanceName;

        if (!prefix.EndsWith(':'))
        {
            prefix += ':';
        }

        if (!prefix.StartsWith(RequiredKeyPrefix, StringComparison.Ordinal))
        {
            prefix = RequiredKeyPrefix;
        }

        return $"{prefix}{key}";
    }
}
