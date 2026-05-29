using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Compliance;

/// <summary>
/// PDPA Compliance Service with durable database persistence.
/// Replaces cache-based ConsentService for legal compliance requirements.
/// </summary>
public sealed class PdpaService
{
    private readonly IErpHubRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<PdpaService> _logger;

    public PdpaService(
        IErpHubRepository repository,
        IMessageBus messageBus,
        ILogger<PdpaService> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task<ConsentRecord?> GetConsentAsync(
        string tenantId,
        string dataSubjectId,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetConsentAsync(tenantId, dataSubjectId, purpose, cancellationToken);
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetConsentsBySubjectAsync(
        string tenantId,
        string dataSubjectId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetConsentsBySubjectAsync(tenantId, dataSubjectId, cancellationToken);
    }

    public async Task<ConsentRecord> GrantConsentAsync(
        string tenantId,
        string dataSubjectId,
        string purpose,
        List<string> doctypes,
        string? notes = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Granting consent for {DataSubjectId} on tenant {TenantId}, purpose {Purpose}",
            dataSubjectId, tenantId, purpose);

        var existing = await _repository.GetConsentAsync(tenantId, dataSubjectId, purpose, cancellationToken);
        if (existing is not null && existing.IsActive)
        {
            throw new InvalidOperationException(
                $"Active consent already exists for subject {dataSubjectId} and purpose {purpose}");
        }

        // Deactivate old consent if exists
        if (existing is not null)
        {
            existing.IsActive = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateConsentAsync(existing, cancellationToken);
        }

        var consent = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Purpose = purpose,
            GrantedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            IsActive = true,
            Notes = notes,
            Doctypes = doctypes ?? [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateConsentAsync(consent, cancellationToken);

        var evt = new ConsentGrantedEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Purpose = purpose,
            GrantedAt = consent.GrantedAt,
            ExpiresAt = expiresAt,
            Doctypes = doctypes ?? []
        };

        await _messageBus.PublishAsync(
            exchange: "",
            routingKey: "compliance.consent.granted",
            evt,
            cancellationToken);

        return consent;
    }

    public async Task<ConsentRecord> WithdrawConsentAsync(
        string tenantId,
        string dataSubjectId,
        string purpose,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Withdrawing consent for {DataSubjectId} on tenant {TenantId}, purpose {Purpose}",
            dataSubjectId, tenantId, purpose);

        var consent = await _repository.GetConsentAsync(tenantId, dataSubjectId, purpose, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No active consent found for subject {dataSubjectId} and purpose {purpose}");

        consent.IsActive = false;
        consent.WithdrawnAt = DateTimeOffset.UtcNow;
        consent.Notes = string.IsNullOrEmpty(consent.Notes)
            ? $"Withdrawn: {reason}"
            : $"{consent.Notes}\nWithdrawn: {reason}";
        consent.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.UpdateConsentAsync(consent, cancellationToken);

        var evt = new ConsentWithdrawnEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Purpose = purpose,
            WithdrawnAt = consent.WithdrawnAt.Value,
            Reason = reason
        };

        await _messageBus.PublishAsync(
            exchange: "",
            routingKey: "compliance.consent.withdrawn",
            evt,
            cancellationToken);

        return consent;
    }

    public async Task<ErasureRequest> RequestDataErasureAsync(
        string tenantId,
        string dataSubjectId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Data erasure requested for {DataSubjectId} on tenant {TenantId}",
            dataSubjectId, tenantId);

        var request = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Status = "pending",
            RequestedAt = DateTimeOffset.UtcNow,
            Notes = reason,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateErasureRequestAsync(request, cancellationToken);

        var evt = new DataErasureRequestedEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DataSubjectId = dataSubjectId,
            Reason = reason,
            RequestedAt = request.RequestedAt
        };

        await _messageBus.PublishAsync(
            exchange: "",
            routingKey: "compliance.erasure.requested",
            evt,
            cancellationToken);

        return request;
    }

    public async Task<ErasureRequest?> GetErasureRequestAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetErasureRequestAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<ErasureRequest>> GetErasureRequestsBySubjectAsync(
        string tenantId,
        string dataSubjectId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetErasureRequestsBySubjectAsync(tenantId, dataSubjectId, cancellationToken);
    }
}

// Events
public sealed class ConsentGrantedEvent
{
    public required string EventId { get; set; }
    public required string TenantId { get; set; }
    public required string DataSubjectId { get; set; }
    public required string Purpose { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public List<string> Doctypes { get; set; } = [];
}
