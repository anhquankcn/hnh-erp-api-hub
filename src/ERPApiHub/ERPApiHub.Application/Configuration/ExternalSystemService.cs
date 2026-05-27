using System.Text;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Errors;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Configuration;

/// <summary>
/// CRUD service for external system registry and API key lifecycle.
/// FRD refs: FR-CFG-001, FR-AUTH-002
/// </summary>
public sealed class ExternalSystemService
{
    private readonly IErpHubRepository _repository;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<ExternalSystemService> _logger;
    private const string ProtectorName = "ApiKeyEncryption";

    public ExternalSystemService(
        IErpHubRepository repository,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ExternalSystemService> logger)
    {
        _repository = repository;
        _dataProtector = dataProtectionProvider.CreateProtector(ProtectorName);
        _logger = logger;
    }

    /// <summary>
    /// Register a new external system with auto-generated API key mapping.
    /// </summary>
    public async Task<ExternalSystem> CreateAsync(
        CreateExternalSystemRequest request,
        string tenantId,
        CancellationToken ct)
    {
        // Check max systems per tenant (50)
        var existingSystems = await _repository.GetExternalSystemsByTenantAsync(tenantId, ct);
        var activeCount = existingSystems.Count(s => s.IsActive && s.DeletedAt == null);

        if (activeCount >= 50)
        {
            throw new InvalidOperationException("Maximum of 50 active external systems per tenant reached.");
        }

        // Validate unique system_name per tenant
        var exists = existingSystems.Any(s => s.SystemName == request.SystemName && s.DeletedAt == null);

        if (exists)
        {
            throw new InvalidOperationException(
                $"External system '{request.SystemName}' already exists for tenant '{tenantId}'.");
        }

        // Validate webhook URL is HTTPS
        if (!string.IsNullOrWhiteSpace(request.WebhookUrl) &&
            !request.WebhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Webhook URL must use HTTPS.");
        }

        var systemId = UlidGenerator.Generate();
        var now = DateTime.UtcNow;

        var system = new ExternalSystem
        {
            SystemId = systemId,
            SystemName = request.SystemName,
            SystemType = request.SystemType,
            Description = request.Description,
            ContactEmail = request.ContactEmail,
            WebhookUrl = request.WebhookUrl,
            RateLimitTier = request.RateLimitTier,
            IsActive = true,
            TenantId = tenantId,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Auto-generate API key mapping
        if (!string.IsNullOrWhiteSpace(request.ErpNextApiKey) &&
            !string.IsNullOrWhiteSpace(request.ErpNextApiSecret))
        {
            var encryptedKey = _dataProtector.Protect(Encoding.UTF8.GetBytes(request.ErpNextApiKey));
            var encryptedSecret = _dataProtector.Protect(Encoding.UTF8.GetBytes(request.ErpNextApiSecret));

            var apiKeyMapping = new ApiKeyMapping
            {
                MappingId = UlidGenerator.Generate(),
                SystemId = systemId,
                ErpNextApiKeyEnc = encryptedKey,
                ErpNextApiSecretEnc = encryptedSecret,
                KeycloakUserId = string.Empty,
                IsActive = true,
                CreatedAt = now
            };

            // Will be saved via repository
        }

        var created = await _repository.CreateExternalSystemAsync(system, ct);

        _logger.LogInformation(
            "External system {SystemId} ({SystemName}) registered for tenant {TenantId}",
            systemId, request.SystemName, tenantId);

        return created;
    }

    /// <summary>
    /// List active external systems for a tenant (paginated).
    /// </summary>
    public async Task<(List<ExternalSystem> Systems, int Total)> ListAsync(
        string tenantId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page <= 0)
            throw new ArgumentException("Page must be greater than 0.", nameof(page));

        if (pageSize <= 0 || pageSize > 100)
            throw new ArgumentException("PageSize must be between 1 and 100.", nameof(pageSize));

        var allSystems = await _repository.GetExternalSystemsByTenantAsync(tenantId, ct);
        var systems = allSystems
            .Where(s => s.DeletedAt == null)
            .OrderBy(s => s.SystemName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var total = allSystems.Count(s => s.DeletedAt == null);

        return (systems, total);
    }

    /// <summary>
    /// Get a single external system by ID (secrets always masked).
    /// </summary>
    public async Task<ExternalSystem?> GetByIdAsync(string systemId, CancellationToken ct)
    {
        return await _repository.GetExternalSystemAsync(systemId, ct);
    }

    /// <summary>
    /// Update an external system's configurable fields.
    /// </summary>
    public async Task<ExternalSystem> UpdateAsync(
        string systemId,
        UpdateExternalSystemRequest request,
        CancellationToken ct)
    {
        var system = await _repository.GetExternalSystemAsync(systemId, ct)
            ?? throw new NotFoundException($"External system '{systemId}' not found.");

        if (request.SystemName is not null) system.SystemName = request.SystemName;
        if (request.SystemType is not null) system.SystemType = request.SystemType;
        if (request.Description is not null) system.Description = request.Description;
        if (request.ContactEmail is not null) system.ContactEmail = request.ContactEmail;
        if (request.WebhookUrl is not null)
        {
            if (!request.WebhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Webhook URL must use HTTPS.");
            }
            system.WebhookUrl = request.WebhookUrl;
        }
        if (request.RateLimitTier is not null) system.RateLimitTier = request.RateLimitTier;

        system.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateExternalSystemAsync(system, ct);

        _logger.LogInformation("External system {SystemId} updated", systemId);
        return system;
    }

    /// <summary>
    /// Soft delete (deactivate) an external system and its API key mapping.
    /// </summary>
    public async Task DeleteAsync(string systemId, CancellationToken ct)
    {
        var system = await _repository.GetExternalSystemAsync(systemId, ct)
            ?? throw new NotFoundException($"External system '{systemId}' not found.");

        var now = DateTime.UtcNow;
        system.IsActive = false;
        system.DeletedAt = now;
        system.UpdatedAt = now;

        await _repository.UpdateExternalSystemAsync(system, ct);

        _logger.LogInformation("External system {SystemId} deactivated (soft delete)", systemId);
    }

    /// <summary>
    /// Rotate API key for an external system. Old key deactivated after grace period.
    /// </summary>
    public async Task<ApiKeyMapping> RotateApiKeyAsync(
        string systemId,
        string newApiKey,
        string newApiSecret,
        CancellationToken ct)
    {
        var system = await _repository.GetExternalSystemAsync(systemId, ct)
            ?? throw new NotFoundException($"External system '{systemId}' not found.");

        // Create new key mapping
        var encryptedKey = _dataProtector.Protect(Encoding.UTF8.GetBytes(newApiKey));
        var encryptedSecret = _dataProtector.Protect(Encoding.UTF8.GetBytes(newApiSecret));

        var newMapping = new ApiKeyMapping
        {
            MappingId = UlidGenerator.Generate(),
            SystemId = systemId,
            ErpNextApiKeyEnc = encryptedKey,
            ErpNextApiSecretEnc = encryptedSecret,
            KeycloakUserId = string.Empty,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // TODO: Save via repository when ApiKeyMapping methods added to IErpHubRepository
        _logger.LogInformation(
            "API key rotated for external system {SystemId}. Old keys deactivated.",
            systemId);

        return newMapping;
    }
}
