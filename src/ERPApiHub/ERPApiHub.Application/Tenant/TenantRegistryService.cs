using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Tenant;

public sealed class TenantRegistryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IErpHubRepository _repository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<TenantRegistryService> _logger;

    public TenantRegistryService(
        IErpHubRepository repository,
        ICacheService cacheService,
        ILogger<TenantRegistryService> logger)
    {
        _repository = repository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<TenantRegistrationResult> RegisterAsync(
        string tenantId,
        string name,
        string keycloakRealm,
        string erpNextBaseUrl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakRealm);
        ArgumentException.ThrowIfNullOrWhiteSpace(erpNextBaseUrl);

        var existing = await _repository.GetTenantRegistryAsync(tenantId, cancellationToken);
        if (existing is not null)
        {
            var existingInfo = ToTenantInfo(existing, keycloakRealm);
            await CacheTenantAsync(existingInfo, cancellationToken);
            return new TenantRegistrationResult(false, existingInfo, $"Tenant '{tenantId}' already exists.");
        }

        var tenant = new TenantRegistry
        {
            TenantId = tenantId,
            SiteName = name,
            ErpNextHost = erpNextBaseUrl,
            HealthStatus = "active",
            IsActive = true
        };

        var created = await _repository.CreateTenantRegistryAsync(tenant, cancellationToken);
        var info = ToTenantInfo(created, keycloakRealm);

        await CacheTenantAsync(info, cancellationToken);
        await _cacheService.RemoveAsync(ListCacheKey, cancellationToken);

        _logger.LogInformation("Tenant {TenantId} registered for ERPNext host {ErpNextHost}", tenantId, erpNextBaseUrl);

        return new TenantRegistrationResult(true, info, null);
    }

    public async Task<TenantInfo?> GetAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var cacheKey = TenantCacheKey(tenantId);
        var cached = await _cacheService.GetAsync<TenantInfo>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var tenant = await _repository.GetTenantRegistryAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var info = ToTenantInfo(tenant);
        await _cacheService.SetAsync(cacheKey, info, CacheTtl, cancellationToken);

        return info;
    }

    public async Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var cached = await _cacheService.GetAsync<IReadOnlyList<TenantInfo>>(ListCacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var tenants = await _repository.ListTenantRegistriesAsync(cancellationToken);
        var result = tenants.Select(t => ToTenantInfo(t)).ToList();

        await _cacheService.SetAsync(ListCacheKey, result, CacheTtl, cancellationToken);

        return result;
    }

    public async Task<TenantHealthStatus> HealthCheckAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var tenant = await _repository.GetTenantRegistryAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return new TenantHealthStatus(tenantId, "not_found", false, DateTimeOffset.UtcNow);
        }

        var isHealthy = tenant.IsActive && string.Equals(tenant.HealthStatus, "active", StringComparison.OrdinalIgnoreCase);
        var status = new TenantHealthStatus(tenant.TenantId, tenant.HealthStatus, isHealthy, DateTimeOffset.UtcNow);

        await _cacheService.SetAsync($"{TenantCacheKey(tenantId)}:health", status, CacheTtl, cancellationToken);

        return status;
    }

    private static string TenantCacheKey(string tenantId) => $"tenant:{tenantId}";

    private const string ListCacheKey = "tenant:list";

    private async Task CacheTenantAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        await _cacheService.SetAsync(TenantCacheKey(tenant.TenantId), tenant, CacheTtl, cancellationToken);
    }

    private static TenantInfo ToTenantInfo(TenantRegistry tenant, string? keycloakRealm = null) =>
        new(
            tenant.TenantId,
            tenant.SiteName,
            keycloakRealm ?? tenant.TenantId,
            tenant.ErpNextHost,
            tenant.HealthStatus,
            tenant.IsActive);
}

public sealed record TenantRegistrationResult(bool Registered, TenantInfo Tenant, string? Message);

public sealed record TenantInfo(
    string TenantId,
    string Name,
    string KeycloakRealm,
    string ErpNextBaseUrl,
    string HealthStatus,
    bool IsActive);

public sealed record TenantHealthStatus(
    string TenantId,
    string Status,
    bool IsHealthy,
    DateTimeOffset CheckedAt);
