using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Audit;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Compliance;

/// <summary>
/// PDPA compliance service: DSAR, consent withdrawal, erasure, export, audit trail.
/// </summary>
public sealed class ConsentService
{
    private readonly IErpHubRepository _repository;
    private readonly ICacheService _cache;
    private readonly IMessageBus _messageBus;
    private readonly PiiMaskingService _masking;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<ConsentService> _logger;

    public ConsentService(
        IErpHubRepository repository,
        ICacheService cache,
        IMessageBus messageBus,
        PiiMaskingService masking,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<ConsentService> logger)
    {
        _repository = repository;
        _cache = cache;
        _messageBus = messageBus;
        _masking = masking;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    // ─── DSAR ─────────────────────────────────────────────

    public async Task<DsarResult> GetDataSubjectAccessRequestAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DSAR requested for tenant {TenantId}", tenantId);

        var tenant = await _repository.GetTenantRegistryAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        var externalSystems = await _repository.GetExternalSystemsByTenantAsync(tenantId, cancellationToken);
        var fieldMappings = new List<FieldMapping>();
        foreach (var system in externalSystems)
        {
            var mappings = await _repository.GetFieldMappingsAsync(system.SystemId, cancellationToken);
            fieldMappings.AddRange(mappings);
        }

        var maskedSystems = externalSystems.Select(s => new MaskedExternalSystem
        {
            SystemId = s.SystemId,
            SystemType = s.SystemType,
            TenantId = MaskTenantId(s.TenantId),
            CreatedAt = s.CreatedAt,
        }).ToList();

        var result = new DsarResult
        {
            TenantId = tenantId,
            RequestedAt = DateTimeOffset.UtcNow,
            ExternalSystems = maskedSystems,
            FieldMappings = fieldMappings.Select(m => m.ErpNextDoctype).Distinct().ToList(),
            PiiDataMasked = true,
        };

        return result;
    }

    // ─── Consent Withdrawal ─────────────────────────────

    public async Task WithdrawConsentAsync(
        string tenantId,
        string purpose,
        string dataSubjectId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Consent withdrawn for {DataSubjectId} on tenant {TenantId}, purpose {Purpose}",
            dataSubjectId, tenantId, purpose);

        var record = new ConsentRecord
        {
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Purpose = purpose,
            Status = "withdrawn",
            GrantedAt = DateTimeOffset.UtcNow.AddYears(-1), // placeholder
            WithdrawnAt = DateTimeOffset.UtcNow,
            WithdrawnReason = reason,
            IpAddress = GetClientIp(),
            UserAgent = GetUserAgent(),
        };

        var cacheKey = $"consent:{tenantId}:{dataSubjectId}:{purpose}";
        await _cache.SetAsync(cacheKey, record, TimeSpan.FromDays(365), cancellationToken);

        var evt = new ConsentWithdrawnEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Purpose = purpose,
            WithdrawnAt = DateTimeOffset.UtcNow,
            Reason = reason,
            CorrelationId = Activity.Current?.Id,
        };

        await _messageBus.PublishAsync(
            exchange: "",
            routingKey: "compliance.consent.withdrawn",
            evt,
            cancellationToken);
    }

    // ─── Data Erasure (Right to be Forgotten) ────────────

    public async Task RequestDataErasureAsync(
        string tenantId,
        string dataSubjectId,
        string reason,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Data erasure requested for {DataSubjectId} on tenant {TenantId}",
            dataSubjectId, tenantId);

        // Soft-delete marker
        var cacheKey = $"erasure:{tenantId}:{dataSubjectId}";
        await _cache.SetAsync(cacheKey, new { Status = "requested", Reason = reason, RequestedAt = DateTimeOffset.UtcNow },
            TimeSpan.FromDays(365), cancellationToken);

        var evt = new DataErasureRequestedEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Reason = reason,
            RequestedAt = DateTimeOffset.UtcNow,
            RequestedBy = requestedBy,
            CorrelationId = Activity.Current?.Id,
        };

        await _messageBus.PublishAsync(
            exchange: "",
            routingKey: "compliance.erasure.requested",
            evt,
            cancellationToken);
    }

    // ─── Data Portability Export ─────────────────────────

    public async Task<ExportJob> RequestDataExportAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("Data export requested for tenant {TenantId}, job {JobId}", tenantId, jobId);

        var job = new ExportJob
        {
            JobId = jobId,
            TenantId = tenantId,
            Status = "queued",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cacheKey = $"export:{tenantId}:{jobId}";
        await _cache.SetAsync(cacheKey, job, TimeSpan.FromDays(7), cancellationToken);

        // Simulate async processing (in production: queue to worker)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                await _cache.SetAsync(cacheKey, job with { Status = "completed", CompletedAt = DateTimeOffset.UtcNow, DownloadUrl = $"/internal/v1/exports/{jobId}" },
                    TimeSpan.FromDays(7), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export job {JobId} failed", jobId);
                try
                {
                    await _cache.SetAsync(cacheKey, job with { Status = "failed", ErrorMessage = ex.Message },
                        TimeSpan.FromDays(7), CancellationToken.None);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogError(cacheEx, "Failed to update export job {JobId} failure status", jobId);
                }
            }
        });

        return job;
    }

    public async Task<ExportJob?> GetExportJobAsync(
        string tenantId,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"export:{tenantId}:{jobId}";
        return await _cache.GetAsync<ExportJob>(cacheKey, cancellationToken);
    }

    // ─── Consent Audit Trail ─────────────────────────────

    public async Task<IReadOnlyList<ConsentRecord>> GetConsentAuditTrailAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Consent audit trail requested for tenant {TenantId}", tenantId);

        // In production: query from database. For now: from cache scan pattern.
        // This is a simplified version.
        return Array.Empty<ConsentRecord>();
    }

    // ─── Helpers ──────────────────────────────────────────

    private string? GetClientIp() =>
        _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() =>
        _httpContextAccessor?.HttpContext?.Request.Headers.UserAgent.ToString();

    private static string MaskTenantId(string tenantId) =>
        tenantId.Length > 4 ? $"{tenantId[..2]}***{tenantId[^2..]}" : "***";
}

public sealed class DsarResult
{
    public required string TenantId { get; init; }
    public required DateTimeOffset RequestedAt { get; init; }
    public required IReadOnlyList<MaskedExternalSystem> ExternalSystems { get; init; }
    public required IReadOnlyList<string> FieldMappings { get; init; }
    public required bool PiiDataMasked { get; init; }
}

public sealed class MaskedExternalSystem
{
    public required string SystemId { get; init; }
    public required string SystemType { get; init; }
    public required string TenantId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
