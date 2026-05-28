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
        CancellationToken cancellationToken = default) =>
        await GetAuditLogsAsync(
            tenantId: tenantId,
            eventType: action,
            fromDate: from,
            toDate: to,
            page: page,
            pageSize: pageSize,
            cancellationToken: cancellationToken);

    public async Task<(IReadOnlyList<AuditLog> Items, int Total)> GetAuditLogsAsync(
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
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(systemId))
        {
            query = query.Where(x => x.SystemId == systemId);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var normalizedEventType = eventType.Trim().ToUpperInvariant();
            query = query.Where(x => x.Method == normalizedEventType);
        }

        if (fromDate is not null)
        {
            query = query.Where(x => x.CreatedAt >= fromDate);
        }

        if (toDate is not null)
        {
            query = query.Where(x => x.CreatedAt <= toDate);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = NormalizeAuditStatus(status) switch
            {
                "success" => query.Where(x => x.StatusCode >= 200 && x.StatusCode < 400),
                "failure" => query.Where(x => x.StatusCode >= 500),
                "warning" => query.Where(x => x.StatusCode == null || x.StatusCode < 200 || (x.StatusCode >= 400 && x.StatusCode < 500)),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            query = query.Where(x => x.Endpoint.Contains(endpoint));
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(x => x.RequestId == correlationId);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var total = await query.CountAsync(cancellationToken);
        var ordered = ApplyAuditLogOrdering(query, sortBy, sortDirection);
        var items = await ordered
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

    private static IOrderedQueryable<AuditLog> ApplyAuditLogOrdering(
        IQueryable<AuditLog> query,
        string? sortBy,
        string? sortDirection)
    {
        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant();

        return normalizedSortBy switch
        {
            "tenantid" or "tenant_id" => descending ? query.OrderByDescending(x => x.TenantId) : query.OrderBy(x => x.TenantId),
            "systemid" or "system_id" => descending ? query.OrderByDescending(x => x.SystemId) : query.OrderBy(x => x.SystemId),
            "eventtype" or "event_type" or "method" => descending ? query.OrderByDescending(x => x.Method) : query.OrderBy(x => x.Method),
            "statuscode" or "status_code" => descending ? query.OrderByDescending(x => x.StatusCode) : query.OrderBy(x => x.StatusCode),
            "durationms" or "duration_ms" => descending ? query.OrderByDescending(x => x.DurationMs) : query.OrderBy(x => x.DurationMs),
            "userid" or "user_id" => descending ? query.OrderByDescending(x => x.UserId) : query.OrderBy(x => x.UserId),
            "endpoint" => descending ? query.OrderByDescending(x => x.Endpoint) : query.OrderBy(x => x.Endpoint),
            _ => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)
        };
    }

    private static string NormalizeAuditStatus(string status) =>
        status.Trim().ToLowerInvariant();

    // PDPA Compliance Methods
    public async Task<ConsentRecord?> GetConsentAsync(string tenantId, string dataSubjectId, string purpose, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ConsentRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.DataSubjectId == dataSubjectId && c.Purpose == purpose && c.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetConsentsBySubjectAsync(string tenantId, string dataSubjectId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ConsentRecords
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DataSubjectId == dataSubjectId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConsentRecord> CreateConsentAsync(ConsentRecord consent, CancellationToken cancellationToken = default)
    {
        _dbContext.ConsentRecords.Add(consent);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return consent;
    }

    public async Task<ConsentRecord> UpdateConsentAsync(ConsentRecord consent, CancellationToken cancellationToken = default)
    {
        _dbContext.ConsentRecords.Update(consent);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return consent;
    }

    public async Task<ErasureRequest?> GetErasureRequestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ErasureRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ErasureRequest>> GetErasureRequestsBySubjectAsync(string tenantId, string dataSubjectId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ErasureRequests
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.DataSubjectId == dataSubjectId)
            .OrderByDescending(e => e.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ErasureRequest> CreateErasureRequestAsync(ErasureRequest request, CancellationToken cancellationToken = default)
    {
        _dbContext.ErasureRequests.Add(request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<ErasureRequest> UpdateErasureRequestAsync(ErasureRequest request, CancellationToken cancellationToken = default)
    {
        _dbContext.ErasureRequests.Update(request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return request;
    }
}
