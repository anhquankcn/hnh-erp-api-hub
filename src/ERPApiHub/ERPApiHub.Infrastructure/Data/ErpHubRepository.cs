using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERPApiHub.Infrastructure.Data;

public sealed class ErpHubRepository(ErpHubDbContext dbContext) : IErpHubRepository
{
    public async Task<ExternalSystem?> GetExternalSystemAsync(string id, CancellationToken cancellationToken = default) =>
        await dbContext.ExternalSystems.FirstOrDefaultAsync(x => x.SystemId == id, cancellationToken);

    public async Task<ExternalSystem?> GetExternalSystemByApiKeyAsync(string apiKeyHash, CancellationToken cancellationToken = default) =>
        await dbContext.ExternalSystems
            .Include(x => x.ApiKeyMappings)
            .FirstOrDefaultAsync(x => x.ApiKeyMappings.Any(m => m.KeycloakUserId == apiKeyHash), cancellationToken);

    public async Task<IReadOnlyList<ExternalSystem>> GetExternalSystemsByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await dbContext.ExternalSystems
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<ExternalSystem> CreateExternalSystemAsync(ExternalSystem system, CancellationToken cancellationToken = default)
    {
        dbContext.ExternalSystems.Add(system);
        await dbContext.SaveChangesAsync(cancellationToken);
        return system;
    }

    public async Task UpdateExternalSystemAsync(ExternalSystem system, CancellationToken cancellationToken = default)
    {
        dbContext.ExternalSystems.Update(system);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteExternalSystemAsync(string id, CancellationToken cancellationToken = default)
    {
        var system = await dbContext.ExternalSystems.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException($"External system {id} not found");

        system.DeletedAt = DateTimeOffset.UtcNow;
        system.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TenantRegistry?> GetTenantRegistryAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await dbContext.TenantRegistries.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

    public async Task<TenantRegistry?> GetTenantRegistryByBranchIdAsync(string branchId, CancellationToken cancellationToken = default) =>
        await dbContext.TenantRegistries.FirstOrDefaultAsync(x => x.TenantId == branchId, cancellationToken);

    public async Task UpdateTenantHealthAsync(
        string tenantId,
        string healthStatus,
        DateTimeOffset? lastHealthCheck = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = await dbContext.TenantRegistries.FindAsync([tenantId], cancellationToken)
            ?? throw new KeyNotFoundException($"Tenant {tenantId} not found");

        tenant.HealthStatus = healthStatus;
        tenant.LastHealthCheck = lastHealthCheck ?? DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TenantRegistry> CreateTenantRegistryAsync(TenantRegistry tenant, CancellationToken cancellationToken = default)
    {
        dbContext.TenantRegistries.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    public async Task<IReadOnlyList<TenantRegistry>> ListTenantRegistriesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.TenantRegistries.OrderBy(x => x.SiteName).ToListAsync(cancellationToken);

    public async Task<ApiKeyMapping?> GetApiKeyMappingAsync(string systemId, CancellationToken cancellationToken = default) =>
        await dbContext.ApiKeyMappings.FirstOrDefaultAsync(x => x.SystemId == systemId && x.IsActive, cancellationToken);

    public async Task<IReadOnlyList<FieldMapping>> GetFieldMappingsAsync(string systemId, CancellationToken cancellationToken = default) =>
        await dbContext.FieldMappings.Where(x => x.SystemId == systemId).ToListAsync(cancellationToken);

    public async Task<AuditLog> CreateAuditLogAsync(AuditLog log, CancellationToken cancellationToken = default)
    {
        dbContext.AuditLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
        return log;
    }

    public async Task<(IReadOnlyList<AuditLog> Items, int Total)> GetAuditLogsAsync(
        string? tenantId = null,
        string? action = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Method == action);
        }

        if (from is not null)
        {
            query = query.Where(x => x.CreatedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(x => x.CreatedAt <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<WebhookSubscription?> GetWebhookSubscriptionAsync(string id, CancellationToken cancellationToken = default) =>
        await dbContext.WebhookSubscriptions.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<WebhookSubscription>> GetWebhookSubscriptionsBySystemAsync(string systemId, CancellationToken cancellationToken = default) =>
        await dbContext.WebhookSubscriptions
            .Where(x => x.SystemId == systemId && x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WebhookSubscription>> GetWebhookSubscriptionsByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await dbContext.WebhookSubscriptions
            .Include(x => x.ExternalSystem)
            .Where(x => x.ExternalSystem != null && x.ExternalSystem.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WebhookSubscription>> GetMatchingWebhookSubscriptionsAsync(string eventType, CancellationToken cancellationToken = default) =>
        await dbContext.WebhookSubscriptions
            .Where(x => x.IsActive && x.EventTypes.Contains(eventType))
            .ToListAsync(cancellationToken);

    public async Task<WebhookSubscription> CreateWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default)
    {
        dbContext.WebhookSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    public async Task UpdateWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default)
    {
        dbContext.WebhookSubscriptions.Update(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteWebhookSubscriptionAsync(string id, CancellationToken cancellationToken = default)
    {
        var subscription = await dbContext.WebhookSubscriptions.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException($"Subscription {id} not found");

        subscription.DeletedAt = DateTimeOffset.UtcNow;
        subscription.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WebhookDelivery> CreateWebhookDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default)
    {
        dbContext.WebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);
        return delivery;
    }

    public async Task UpdateWebhookDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default)
    {
        dbContext.WebhookDeliveries.Update(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetWebhookDeliveriesAsync(string subscriptionId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
        await dbContext.WebhookDeliveries
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderByDescending(x => x.AttemptedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<ErpProcessedEvent?> GetProcessedEventAsync(string eventId, CancellationToken cancellationToken = default) =>
        await dbContext.ErpProcessedEvents.FirstOrDefaultAsync(x => x.ErpProcessedEventId == eventId, cancellationToken);

    public async Task CreateProcessedEventAsync(ErpProcessedEvent evt, CancellationToken cancellationToken = default)
    {
        dbContext.ErpProcessedEvents.Add(evt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLog>> GetAuditLogsOlderThanAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogs
            .Where(x => x.CreatedAt < cutoff && x.ArchiveStatus == null)
            .OrderBy(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task MarkAuditLogsArchivingAsync(IReadOnlyList<string> ids, DateTimeOffset claimedAt, CancellationToken cancellationToken = default)
    {
        var logs = await dbContext.AuditLogs
            .Where(x => ids.Contains(x.LogId) && x.ArchiveStatus == null)
            .ToListAsync(cancellationToken);

        foreach (var log in logs)
        {
            log.ArchiveStatus = "archiving";
            log.ArchiveClaimedAt = claimedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAuditLogsArchivingAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var logs = await dbContext.AuditLogs
            .Where(x => ids.Contains(x.LogId) && x.ArchiveStatus == "archiving")
            .ToListAsync(cancellationToken);

        foreach (var log in logs)
        {
            log.ArchiveStatus = null;
            log.ArchiveClaimedAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountAuditLogsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogs.CountAsync(cancellationToken);

    public async Task<int> CountAuditLogsOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogs.CountAsync(x => x.CreatedAt < cutoff && x.ArchiveStatus == null, cancellationToken);

    public async Task DeleteAuditLogsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var logs = await dbContext.AuditLogs
            .Where(x => ids.Contains(x.LogId))
            .ToListAsync(cancellationToken);

        dbContext.AuditLogs.RemoveRange(logs);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
