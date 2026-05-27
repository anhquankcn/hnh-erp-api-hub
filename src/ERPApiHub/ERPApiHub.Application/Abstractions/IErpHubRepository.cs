using ERPApiHub.Domain.Entities;

namespace ERPApiHub.Application.Abstractions;

public interface IErpHubRepository
{
    // External Systems
    Task<ExternalSystem?> GetExternalSystemAsync(string id, CancellationToken cancellationToken = default);
    Task<ExternalSystem?> GetExternalSystemByApiKeyAsync(string apiKeyHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalSystem>> GetExternalSystemsByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<ExternalSystem> CreateExternalSystemAsync(ExternalSystem system, CancellationToken cancellationToken = default);
    Task UpdateExternalSystemAsync(ExternalSystem system, CancellationToken cancellationToken = default);
    Task DeleteExternalSystemAsync(string id, CancellationToken cancellationToken = default);

    // Tenant Registry
    Task<TenantRegistry?> GetTenantRegistryAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<TenantRegistry?> GetTenantRegistryByBranchIdAsync(string branchId, CancellationToken cancellationToken = default);
    Task UpdateTenantHealthAsync(string tenantId, string healthStatus, CancellationToken cancellationToken = default);

    // API Key Mappings
    Task<ApiKeyMapping?> GetApiKeyMappingAsync(string systemId, CancellationToken cancellationToken = default);

    // Field Mappings
    Task<IReadOnlyList<FieldMapping>> GetFieldMappingsAsync(string systemId, CancellationToken cancellationToken = default);

    // Audit Logs
    Task<AuditLog> CreateAuditLogAsync(AuditLog log, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<AuditLog> Items, int Total)> GetAuditLogsAsync(
        string? tenantId = null,
        string? action = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    // Webhooks
    Task<WebhookSubscription?> GetWebhookSubscriptionAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookSubscription>> GetWebhookSubscriptionsBySystemAsync(string systemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookSubscription>> GetWebhookSubscriptionsByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookSubscription>> GetMatchingWebhookSubscriptionsAsync(string eventType, CancellationToken cancellationToken = default);
    Task<WebhookSubscription> CreateWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default);
    Task UpdateWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default);
    Task DeleteWebhookSubscriptionAsync(string id, CancellationToken cancellationToken = default);

    // Webhook Deliveries
    Task<WebhookDelivery> CreateWebhookDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default);
    Task UpdateWebhookDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookDelivery>> GetWebhookDeliveriesAsync(string subscriptionId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);

    // Processed Events
    Task<ErpProcessedEvent?> GetProcessedEventAsync(string eventId, CancellationToken cancellationToken = default);
    Task CreateProcessedEventAsync(ErpProcessedEvent evt, CancellationToken cancellationToken = default);

    // Save Changes
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
