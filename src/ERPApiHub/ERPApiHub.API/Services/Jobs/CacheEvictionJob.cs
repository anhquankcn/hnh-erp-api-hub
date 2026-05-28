using ERPApiHub.API.Services.Caching;
using ERPApiHub.Infrastructure.Caching;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ERPApiHub.API.Services.Jobs;

public sealed class CacheEvictionJob(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<CacheOptions> cacheOptions,
    IOptions<RedisOptions> redisOptions,
    IOptions<JobOptions> jobOptions,
    ILogger<CacheEvictionJob> logger)
{
    public Task CleanupExpiredEntriesAsync() => CleanupExpiredEntriesAsync(CancellationToken.None);

    private async Task CleanupExpiredEntriesAsync(CancellationToken cancellationToken)
    {
        if (!jobOptions.Value.Enabled)
        {
            return;
        }

        var database = connectionMultiplexer.GetDatabase();
        var removedIndexEntries = await RemoveExpiredKeyIndexEntriesAsync(database, cancellationToken);
        var removedTagEntries = await RemoveExpiredTagIndexEntriesAsync(database, cancellationToken);

        logger.LogInformation(
            "Cache eviction cleanup removed {KeyIndexEntries} expired key index entries and {TagIndexEntries} expired tag index entries.",
            removedIndexEntries,
            removedTagEntries);
    }

    private async Task<int> RemoveExpiredKeyIndexEntriesAsync(IDatabase database, CancellationToken cancellationToken)
    {
        var indexKey = BuildRedisKey(cacheOptions.Value.KeyIndexSet);
        var removed = 0;

        await foreach (var member in database.SetScanAsync(indexKey).WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!member.HasValue)
            {
                continue;
            }

            var redisKey = (RedisKey)member.ToString();
            if (!await database.KeyExistsAsync(redisKey))
            {
                await database.SetRemoveAsync(indexKey, member);
                removed++;
            }
        }

        return removed;
    }

    private async Task<int> RemoveExpiredTagIndexEntriesAsync(IDatabase database, CancellationToken cancellationToken)
    {
        var removed = 0;
        var tagPattern = BuildRedisKey($"{cacheOptions.Value.TagKeyPrefix}*").ToString();

        foreach (var endpoint in connectionMultiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var server = connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica)
            {
                continue;
            }

            await foreach (var tagKey in server.KeysAsync(pattern: tagPattern).WithCancellation(cancellationToken))
            {
                await foreach (var member in database.SetScanAsync(tagKey).WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!member.HasValue)
                    {
                        continue;
                    }

                    var redisKey = (RedisKey)member.ToString();
                    if (!await database.KeyExistsAsync(redisKey))
                    {
                        await database.SetRemoveAsync(tagKey, member);
                        removed++;
                    }
                }

                if (await database.SetLengthAsync(tagKey) == 0)
                {
                    await database.KeyDeleteAsync(tagKey);
                }
            }
        }

        return removed;
    }

    private RedisKey BuildRedisKey(string key)
    {
        var prefix = !string.IsNullOrWhiteSpace(cacheOptions.Value.RedisKeyPrefix)
            ? cacheOptions.Value.RedisKeyPrefix
            : redisOptions.Value.InstanceName;

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
}
