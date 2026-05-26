using System.Net;
using System.Text;
using System.Text.Json;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetUlid;

namespace ERPApiHub.Application.Webhooks;

public sealed class WebhookSubscriptionService
{
    private readonly ErpHubDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<WebhookSubscriptionService> _logger;

    public WebhookSubscriptionService(
        ErpHubDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<WebhookSubscriptionService> logger)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector("WebhookSecret");
        _logger = logger;
    }

    public async Task<WebhookSubscription> CreateAsync(string systemId, string[] eventTypes, string webhookUrl, string? secret, string tenantId, CancellationToken ct)
    {
        var subscription = new WebhookSubscription
        {
            SubscriptionId = Ulid.NewUlid().ToString(),
            SystemId = systemId,
            EventTypes = eventTypes,
            WebhookUrl = webhookUrl,
            IsActive = true
        };

        if (!string.IsNullOrWhiteSpace(secret))
        {
            subscription.SecretEncrypted = _dataProtector.Protect(Encoding.UTF8.GetBytes(secret));
        }

        _dbContext.WebhookSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Created webhook subscription {SubId} for system {SysId}", subscription.SubscriptionId, systemId);
        return subscription;
    }

    public async Task<List<WebhookSubscription>> ListByTenantAsync(string tenantId, CancellationToken ct)
    {
        return await _dbContext.WebhookSubscriptions
            .Include(s => s.ExternalSystem)
            .Where(s => s.ExternalSystem != null && s.ExternalSystem.TenantId == tenantId && s.IsActive)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<WebhookSubscription?> GetByIdAsync(string subscriptionId, CancellationToken ct)
    {
        return await _dbContext.WebhookSubscriptions
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId && s.DeletedAt == null, ct);
    }

    public async Task<WebhookSubscription> UpdateAsync(string subscriptionId, string[]? eventTypes, string? webhookUrl, string? secret, CancellationToken ct)
    {
        var subscription = await _dbContext.WebhookSubscriptions.FindAsync([subscriptionId], ct)
            ?? throw new KeyNotFoundException($"Subscription {subscriptionId} not found");

        if (eventTypes is not null) subscription.EventTypes = eventTypes;
        if (webhookUrl is not null) subscription.WebhookUrl = webhookUrl;
        if (secret is not null) subscription.SecretEncrypted = _dataProtector.Protect(Encoding.UTF8.GetBytes(secret));

        await _dbContext.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task DeleteAsync(string subscriptionId, CancellationToken ct)
    {
        var subscription = await _dbContext.WebhookSubscriptions.FindAsync([subscriptionId], ct)
            ?? throw new KeyNotFoundException($"Subscription {subscriptionId} not found");

        subscription.DeletedAt = DateTimeOffset.UtcNow;
        subscription.IsActive = false;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Soft-deleted webhook subscription {SubId}", subscriptionId);
    }

    public async Task<List<WebhookSubscription>> FindMatchingSubscriptionsAsync(string eventType, CancellationToken ct)
    {
        return await _dbContext.WebhookSubscriptions
            .Where(s => s.IsActive && s.EventTypes.Contains(eventType))
            .ToListAsync(ct);
    }
}