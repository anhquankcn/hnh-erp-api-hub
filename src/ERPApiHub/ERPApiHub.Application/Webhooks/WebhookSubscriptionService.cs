using System.Text;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Webhooks;

public sealed class WebhookSubscriptionService
{
    private readonly IErpHubRepository _repository;
    private readonly IDataProtector _dataProtector;
    private readonly WebhookEndpointValidator _endpointValidator;
    private readonly ILogger<WebhookSubscriptionService> _logger;

    public WebhookSubscriptionService(
        IErpHubRepository repository,
        IDataProtectionProvider dataProtectionProvider,
        WebhookEndpointValidator endpointValidator,
        ILogger<WebhookSubscriptionService> logger)
    {
        _repository = repository;
        _dataProtector = dataProtectionProvider.CreateProtector("WebhookSecret");
        _endpointValidator = endpointValidator;
        _logger = logger;
    }

    public async Task<WebhookSubscription> CreateAsync(string systemId, string[] eventTypes, string webhookUrl, string? secret, string tenantId, CancellationToken ct)
    {
        var endpointValidation = await _endpointValidator.ValidateAsync(webhookUrl, ct);
        if (!endpointValidation.IsValid)
        {
            throw new ArgumentException(endpointValidation.Error, nameof(webhookUrl));
        }

        var existing = await _repository.GetWebhookSubscriptionsBySystemAsync(systemId, ct);
        if (existing.Count >= 10)
        {
            throw new InvalidOperationException($"System {systemId} already has maximum of 10 webhook subscriptions");
        }
        var subscription = new WebhookSubscription
        {
            SubscriptionId = UlidGenerator.Generate(),
            SystemId = systemId,
            EventTypes = eventTypes,
            WebhookUrl = webhookUrl,
            IsActive = true
        };

        if (!string.IsNullOrWhiteSpace(secret))
        {
            subscription.SecretEncrypted = _dataProtector.Protect(Encoding.UTF8.GetBytes(secret));
        }

        await _repository.CreateWebhookSubscriptionAsync(subscription, ct);

        _logger.LogInformation("Created webhook subscription {SubId} for system {SysId}", subscription.SubscriptionId, systemId);
        return subscription;
    }

    public async Task<List<WebhookSubscription>> ListByTenantAsync(string tenantId, CancellationToken ct)
    {
        return [.. await _repository.GetWebhookSubscriptionsByTenantAsync(tenantId, ct)];
    }

    public async Task<WebhookSubscription?> GetByIdAsync(string subscriptionId, CancellationToken ct)
    {
        return await _repository.GetWebhookSubscriptionAsync(subscriptionId, ct);
    }

    public async Task<WebhookSubscription> UpdateAsync(string subscriptionId, string[]? eventTypes, string? webhookUrl, string? secret, CancellationToken ct)
    {
        var subscription = await _repository.GetWebhookSubscriptionAsync(subscriptionId, ct)
            ?? throw new KeyNotFoundException($"Subscription {subscriptionId} not found");

        if (eventTypes is not null) subscription.EventTypes = eventTypes;
        if (webhookUrl is not null)
        {
            var endpointValidation = await _endpointValidator.ValidateAsync(webhookUrl, ct);
            if (!endpointValidation.IsValid)
            {
                throw new ArgumentException(endpointValidation.Error, nameof(webhookUrl));
            }

            subscription.WebhookUrl = webhookUrl;
        }

        if (secret is not null) subscription.SecretEncrypted = _dataProtector.Protect(Encoding.UTF8.GetBytes(secret));

        await _repository.UpdateWebhookSubscriptionAsync(subscription, ct);
        return subscription;
    }

    public async Task DeleteAsync(string subscriptionId, CancellationToken ct)
    {
        var subscription = await _repository.GetWebhookSubscriptionAsync(subscriptionId, ct)
            ?? throw new KeyNotFoundException($"Subscription {subscriptionId} not found");

        subscription.DeletedAt = DateTimeOffset.UtcNow;
        subscription.IsActive = false;
        await _repository.UpdateWebhookSubscriptionAsync(subscription, ct);

        _logger.LogInformation("Soft-deleted webhook subscription {SubId}", subscriptionId);
    }

    public async Task<List<WebhookSubscription>> FindMatchingSubscriptionsAsync(string eventType, CancellationToken ct)
    {
        return [.. await _repository.GetMatchingWebhookSubscriptionsAsync(eventType, ct)];
    }
}
