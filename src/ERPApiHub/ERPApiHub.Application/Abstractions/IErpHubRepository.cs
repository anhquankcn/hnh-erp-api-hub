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
    Task<TenantRegistry> CreateTenantRegistryAsync(TenantRegistry tenant, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantRegistry>> ListTenantRegistriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantRegistry>> ListTenantRegistriesAsync(bool onlyActive, CancellationToken cancellationToken = default);
    Task UpdateTenantHealthAsync(
        string tenantId,
        string healthStatus,
        DateTimeOffset? lastHealthCheck = null,
        CancellationToken cancellationToken = default);

    // API Key Mappings
    Task<ApiKeyMapping?> GetApiKeyMappingAsync(string systemId, CancellationToken cancellationToken = default);

    // Field Mappings
    Task<IReadOnlyList<FieldMapping>> GetFieldMappingsAsync(string systemId, CancellationToken cancellationToken = default);

    // Audit Logs
    Task<AuditLog> CreateAuditLogAsync(AuditLog log, CancellationToken cancellationToken = default);

    // PDPA Compliance
    Task<ConsentRecord> GetConsentAsync(string tenantId, string dataSubjectId, string purpose, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsentRecord>> GetConsentsBySubjectAsync(string tenantId, string dataSubjectId, CancellationToken cancellationToken = default);
    Task<ConsentRecord> CreateConsentAsync(ConsentRecord consent, CancellationToken cancellationToken = default);
    Task<ConsentRecord> UpdateConsentAsync(ConsentRecord consent, CancellationToken cancellationToken = default);
    Task<ErasureRequest> GetErasureRequestAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ErasureRequest>> GetErasureRequestsBySubjectAsync(string tenantId, string dataSubjectId, CancellationToken cancellationToken = default);
    Task<ErasureRequest> CreateErasureRequestAsync(ErasureRequest request, CancellationToken cancellationToken = default);
    Task<ErasureRequest> UpdateErasureRequestAsync(ErasureRequest request, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<AuditLog> Items, int Total)> GetAuditLogsAsync(
        string? tenantId = null,
        string? action = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<AuditLog> Items, int Total)> GetAuditLogsAsync(
        string? tenantId = null,
        string? systemId = null,
        string? eventType = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        string? status = null,
        string? userId = null,
        string? endpoint = null,
        string? correlationId = null,
        int page = 1,
        int pageSize = 50,
        string sortBy = "createdAt",
        string sortDirection = "desc",
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditLog>> GetAuditLogsOlderThanAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken = default);
    Task MarkAuditLogsArchivingAsync(IReadOnlyList<string> ids, DateTimeOffset claimedAt, CancellationToken cancellationToken = default);
    Task ClearAuditLogsArchivingAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default);
    Task<int> CountAuditLogsAsync(CancellationToken cancellationToken = default);
    Task<int> CountAuditLogsOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task DeleteAuditLogsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default);

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
