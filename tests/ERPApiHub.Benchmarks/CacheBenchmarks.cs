using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using ERPApiHub.API.Services.Caching;
using ERPApiHub.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace ERPApiHub.Benchmarks;

[MemoryDiagnoser]
public class CacheBenchmarks
{
    private readonly ConcurrentDictionary<string, string> _redisStore = new();
    private CacheService _cache = null!;
    private int _factoryCalls;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var database = CreateDatabaseMock();
        var connection = new Mock<IConnectionMultiplexer>();
        connection
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);

        _cache = new CacheService(
            new MemoryCache(new MemoryCacheOptions()),
            connection.Object,
            Options.Create(new CacheOptions
            {
                Enabled = true,
                DefaultTtl = TimeSpan.FromMinutes(5),
                L1Ttl = TimeSpan.FromMinutes(1),
                RedisKeyPrefix = "bench:",
                TagKeyPrefix = "tag:",
                KeyIndexSet = "keys"
            }),
            Options.Create(new RedisOptions()),
            NullLogger<CacheService>.Instance);

        await _cache.SetAsync("cache:l1-hit", new CachePayload("warm-l1", 42), TimeSpan.FromMinutes(5));
        _redisStore["bench:cache:l2-hit"] = """{"name":"warm-l2","value":84}""";
    }

    [IterationSetup(Target = nameof(CacheMissL2Hit))]
    public Task ClearL1ForL2Hit() => _cache.RemoveAsync("cache:l2-hit", CacheLevel.L1);

    [IterationSetup(Target = nameof(CacheStampedeConcurrentRequests))]
    public Task ClearStampedeKey()
    {
        _factoryCalls = 0;
        _redisStore.TryRemove("bench:cache:stampede", out _);
        return _cache.RemoveAsync("cache:stampede", CacheLevel.L1);
    }

    [Benchmark(Baseline = true)]
    public async Task<CachePayload?> CacheHitL1()
    {
        return await _cache.GetAsync<CachePayload>("cache:l1-hit");
    }

    [Benchmark]
    public async Task<CachePayload?> CacheMissL2Hit()
    {
        return await _cache.GetAsync<CachePayload>("cache:l2-hit");
    }

    [Benchmark]
    public async Task<CachePayload[]> CacheStampedeConcurrentRequests()
    {
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _cache.GetOrCreateAsync(
                "cache:stampede",
                async cancellationToken =>
                {
                    Interlocked.Increment(ref _factoryCalls);
                    await Task.Delay(10, cancellationToken);
                    return new CachePayload("created-once", _factoryCalls);
                },
                TimeSpan.FromMinutes(5)))
            .ToArray();

        return await Task.WhenAll(tasks);
    }

    private Mock<IDatabase> CreateDatabaseMock()
    {
        var database = new Mock<IDatabase>();

        database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags flags) =>
                _redisStore.TryGetValue(key.ToString(), out var value)
                    ? (RedisValue)value
                    : RedisValue.Null);

        database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? expiry, bool keepTtl, When when, CommandFlags flags) =>
            {
                _redisStore[key.ToString()] = value.ToString();
                return true;
            });

        database
            .Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags flags) => _redisStore.TryRemove(key.ToString(), out _));

        database
            .Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags flags) => _redisStore.ContainsKey(key.ToString()));

        return database;
    }

    private sealed record CachePayload(string Name, int Value);
}
