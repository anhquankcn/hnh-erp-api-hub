using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Ingestion;

public sealed class DlqManagementService
{
    private readonly IMessageBus _messageBus;
    private readonly ICacheService _cache;
    private readonly ILogger<DlqManagementService> _logger;

    public DlqManagementService(IMessageBus messageBus, ICacheService cache, ILogger<DlqManagementService> logger)
    {
        _messageBus = messageBus;
        _cache = cache;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<DlqMessage> Items, int Total)> GetDeadLettersAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var key = "erphub:dlq:messages";
        var messages = await _cache.GetAsync<List<DlqMessage>>(key, cancellationToken) ?? [];
        var total = messages.Count;
        var items = messages.OrderByDescending(m => m.FailedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return (items, total);
    }

    public async Task ReplayAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var key = "erphub:dlq:messages";
        var messages = await _cache.GetAsync<List<DlqMessage>>(key, cancellationToken) ?? [];
        var message = messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null)
        {
            _logger.LogWarning("DLQ message {MessageId} not found for replay", messageId);
            return;
        }

        await _messageBus.PublishAsync(string.Empty, message.RoutingKey, message.Payload, cancellationToken);
        _logger.LogInformation("Replayed DLQ message {MessageId} to {RoutingKey}", messageId, message.RoutingKey);
    }

    public async Task PurgeAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var key = "erphub:dlq:messages";
        var messages = await _cache.GetAsync<List<DlqMessage>>(key, cancellationToken) ?? [];
        messages.RemoveAll(m => m.Id == messageId);
        await _cache.SetAsync(key, messages, TimeSpan.FromDays(7), cancellationToken);
        _logger.LogInformation("Purged DLQ message {MessageId}", messageId);
    }
}

public sealed class DlqMessage
{
    public string Id { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public DateTimeOffset FailedAt { get; set; }
}
