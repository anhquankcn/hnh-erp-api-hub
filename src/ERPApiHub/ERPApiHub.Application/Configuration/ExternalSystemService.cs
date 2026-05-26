using System.Text;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetUlid;

namespace ERPApiHub.Application.Configuration;

/// <summary>
/// CRUD service for external system registry and API key lifecycle.
/// FRD refs: FR-CFG-001, FR-AUTH-002
/// </summary>
public sealed class ExternalSystemService
{
    private readonly ErpHubDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<ExternalSystemService> _logger;
    private const string ProtectorName = "ApiKeyEncryption";

    public ExternalSystemService(
        ErpHubDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ExternalSystemService> logger)
    {
        _dbContext = dbContext;
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
        // Validate unique system_name per tenant
        var exists = await _dbContext.ExternalSystems
            .AnyAsync(s => s.TenantId == tenantId && s.SystemName == request.SystemName && s.DeletedAt == null, ct);

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

        // Check max systems per tenant (50)
        var activeCount = await _dbContext.ExternalSystems
            .CountAsync(s => s.TenantId == tenantId && s.IsActive && s.DeletedAt == null, ct);

        if (activeCount >= 50)
        {
            throw new InvalidOperationException("Maximum of 50 active external systems per tenant reached.");
        }

        var systemId = Ulid.NewUlid().ToString();
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

        _dbContext.ExternalSystems.Add(system);

        // Auto-generate API key mapping
        if (!string.IsNullOrWhiteSpace(request.ErpNextApiKey) &&
            !string.IsNullOrWhiteSpace(request.ErpNextApiSecret))
        {
            var encryptedKey = _dataProtector.Protect(Encoding.UTF8.GetBytes(request.ErpNextApiKey));
            var encryptedSecret = _dataProtector.Protect(Encoding.UTF8.GetBytes(request.ErpNextApiSecret));

            var apiKeyMapping = new ApiKeyMapping
            {
                MappingId = Ulid.NewUlid().ToString(),
                ExternalSystemId = systemId,
                ErpNextApiKeyEnc = encryptedKey,
                ErpNextApiSecretEnc = encryptedSecret,
                KeycloakUserId = string.Empty,
                IsActive = true,
                CreatedAt = now
            };

            _dbContext.ApiKeyMappings.Add(apiKeyMapping);
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "External system {SystemId} ({SystemName}) registered for tenant {TenantId}",
            systemId, request.SystemName, tenantId);

        return system;
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
        var query = _dbContext.ExternalSystems
            .Where(s => s.TenantId == tenantId && s.DeletedAt == null);

        var total = await query.CountAsync(ct);

        var systems = await query
            .OrderBy(s => s.SystemName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (systems, total);
    }

    /// <summary>
    /// Get a single external system by ID (secrets always masked).
    /// </summary>
    public async Task<ExternalSystem?> GetByIdAsync(string systemId, CancellationToken ct)
    {
        return await _dbContext.ExternalSystems
            .FirstOrDefaultAsync(s => s.SystemId == systemId && s.DeletedAt == null, ct);
    }

    /// <summary>
    /// Update an external system's configurable fields.
    /// </summary>
    public async Task<ExternalSystem> UpdateAsync(
        string systemId,
        UpdateExternalSystemRequest request,
        CancellationToken ct)
    {
        var system = await _dbContext.ExternalSystems
            .FirstOrDefaultAsync(s => s.SystemId == systemId && s.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"External system '{systemId}' not found.");

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

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("External system {SystemId} updated", systemId);
        return system;
    }

    /// <summary>
    /// Soft delete (deactivate) an external system and its API key mapping.
    /// </summary>
    public async Task DeleteAsync(string systemId, CancellationToken ct)
    {
        var system = await _dbContext.ExternalSystems
            .FirstOrDefaultAsync(s => s.SystemId == systemId && s.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"External system '{systemId}' not found.");

        var now = DateTime.UtcNow;
        system.IsActive = false;
        system.DeletedAt = now;
        system.UpdatedAt = now;

        // Deactivate API key mappings
        var mappings = await _dbContext.ApiKeyMappings
            .Where(m => m.ExternalSystemId == systemId && m.IsActive)
            .ToListAsync(ct);

        foreach (var mapping in mappings)
        {
            mapping.IsActive = false;
        }

        await _dbContext.SaveChangesAsync(ct);

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
        var system = await _dbContext.ExternalSystems
            .FirstOrDefaultAsync(s => s.SystemId == systemId && s.DeletedAt == null, ct)
            ?? throw new InvalidOperationException($"External system '{systemId}' not found.");

        // Deactivate old keys (immediate — grace period would need background job)
        var oldMappings = await _dbContext.ApiKeyMappings
            .Where(m => m.ExternalSystemId == systemId && m.IsActive)
            .ToListAsync(ct);

        foreach (var mapping in oldMappings)
        {
            mapping.IsActive = false;
        }

        // Create new key mapping
        var encryptedKey = _dataProtector.Protect(Encoding.UTF8.GetBytes(newApiKey));
        var encryptedSecret = _dataProtector.Protect(Encoding.UTF8.GetBytes(newApiSecret));

        var newMapping = new ApiKeyMapping
        {
            MappingId = Ulid.NewUlid().ToString(),
            ExternalSystemId = systemId,
            ErpNextApiKeyEnc = encryptedKey,
            ErpNextApiSecretEnc = encryptedSecret,
            KeycloakUserId = string.Empty,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ApiKeyMappings.Add(newMapping);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "API key rotated for external system {SystemId}. Old keys deactivated.",
            systemId);

        return newMapping;
    }
}