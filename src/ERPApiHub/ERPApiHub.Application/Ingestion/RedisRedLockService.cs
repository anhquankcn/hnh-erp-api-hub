using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ERPApiHub.Application.Ingestion;

public sealed class RedisRedLockService : IRedLockService
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisRedLockService> _logger;

    public RedisRedLockService(IConnectionMultiplexer redis, ILogger<RedisRedLockService> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<bool> AcquireLockAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        cancellationToken.ThrowIfCancellationRequested();

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "Lock TTL must be greater than zero.");
        }

        const string luaScript = """
            return redis.call('SET', KEYS[1], ARGV[1], 'NX', 'EX', ARGV[2]) and 1 or 0
        """;

        try
        {
            var ttlSeconds = Math.Max(1, (int)Math.Ceiling(ttl.TotalSeconds));
            var acquired = (long)await _database.ScriptEvaluateAsync(
                luaScript,
                new RedisKey[] { BuildLockKey(resource) },
                new RedisValue[] { "locked", ttlSeconds });

            return acquired == 1;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis failed while acquiring RedLock for resource {Resource}", resource);
            return false;
        }
    }

    public async Task ReleaseLockAsync(string resource, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _database.KeyDeleteAsync(BuildLockKey(resource));
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis failed while releasing RedLock for resource {Resource}", resource);
            throw;
        }
    }

    private static RedisKey BuildLockKey(string resource) => $"erphub:redlock:{resource}";
}
