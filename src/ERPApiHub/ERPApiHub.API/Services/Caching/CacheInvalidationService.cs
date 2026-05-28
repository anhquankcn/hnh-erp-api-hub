using ERPApiHub.Application.Abstractions;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.API.Services.Caching;

public sealed class CacheInvalidationService(
    CacheService cacheService,
    IMessageBus messageBus,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<CacheInvalidationService> logger)
{
    private const string RoutingKey = "erphub.cache.invalidate";

    public async Task<CacheInvalidationResult> InvalidateKeyAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await cacheService.RemoveAsync(key, cancellationToken);
        await BroadcastAsync(CacheInvalidationMessage.ForKey(key), cancellationToken);
        return new CacheInvalidationResult("key", key, 1);
    }

    public async Task<CacheInvalidationResult> InvalidatePatternAsync(
        string pattern,
        CancellationToken cancellationToken = default)
    {
        var removed = await cacheService.RemoveByPatternAsync(pattern, cancellationToken);
        await BroadcastAsync(CacheInvalidationMessage.ForPattern(pattern), cancellationToken);
        return new CacheInvalidationResult("pattern", pattern, removed);
    }

    public async Task<CacheInvalidationResult> InvalidateTagAsync(
        string tag,
        CancellationToken cancellationToken = default)
    {
        var removed = await cacheService.RemoveByTagAsync(tag, cancellationToken);
        await BroadcastAsync(CacheInvalidationMessage.ForTag(tag), cancellationToken);
        return new CacheInvalidationResult("tag", tag, removed);
    }

    public async Task<CacheInvalidationResult> InvalidateDoctypeAsync(
        string doctype,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(tenantId)
            ? $"doctype:{doctype}"
            : $"query:{tenantId}:{doctype}";

        var removed = await cacheService.RemoveByTagAsync(target, cancellationToken);
        if (removed == 0)
        {
            var pattern = string.IsNullOrWhiteSpace(tenantId)
                ? $"query:*:{doctype}:*"
                : $"query:{tenantId}:{doctype}:*";

            removed = await cacheService.RemoveByPatternAsync(pattern, cancellationToken);
        }

        await BroadcastAsync(CacheInvalidationMessage.ForDoctype(doctype, tenantId), cancellationToken);
        return new CacheInvalidationResult("doctype", target, removed);
    }

    public async Task<IReadOnlyCollection<CacheInvalidationResult>> PurgeAsync(
        CachePurgeRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CacheInvalidationResult>();

        foreach (var key in request.Keys)
        {
            results.Add(await InvalidateKeyAsync(key, cancellationToken));
        }

        foreach (var pattern in request.Patterns)
        {
            results.Add(await InvalidatePatternAsync(pattern, cancellationToken));
        }

        foreach (var tag in request.Tags)
        {
            results.Add(await InvalidateTagAsync(tag, cancellationToken));
        }

        foreach (var doctype in request.Doctypes)
        {
            results.Add(await InvalidateDoctypeAsync(doctype, request.TenantId, cancellationToken));
        }

        return results;
    }

    private async Task BroadcastAsync(CacheInvalidationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await messageBus.PublishAsync(
                rabbitMqOptions.Value.ExchangeName,
                RoutingKey,
                message,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast cache invalidation {InvalidationType} {Target}.", message.Type, message.Target);
        }
    }
}

public sealed record CachePurgeRequest
{
    public string[] Keys { get; init; } = [];

    public string[] Patterns { get; init; } = [];

    public string[] Tags { get; init; } = [];

    public string[] Doctypes { get; init; } = [];

    public string? TenantId { get; init; }
}

public sealed record CacheInvalidationResult(string Type, string Target, int RemovedCount);

public sealed record CacheInvalidationMessage
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    public string Type { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string? TenantId { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public static CacheInvalidationMessage ForKey(string key) =>
        new() { Type = "key", Target = key };

    public static CacheInvalidationMessage ForPattern(string pattern) =>
        new() { Type = "pattern", Target = pattern };

    public static CacheInvalidationMessage ForTag(string tag) =>
        new() { Type = "tag", Target = tag };

    public static CacheInvalidationMessage ForDoctype(string doctype, string? tenantId) =>
        new() { Type = "doctype", Target = doctype, TenantId = tenantId };
}
