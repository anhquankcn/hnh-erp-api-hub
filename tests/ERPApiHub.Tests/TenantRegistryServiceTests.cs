using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Tenant;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class TenantRegistryServiceTests
{
    private readonly Mock<IErpHubRepository> _repository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<TenantRegistryService>> _logger = new();

    private TenantRegistryService CreateService() => new(
        _repository.Object,
        _cache.Object,
        _logger.Object);

    [Fact]
    public async Task RegisterAsync_WhenTenantDoesNotExist_CreatesNewTenant()
    {
        _repository.Setup(x => x.GetTenantRegistryAsync("tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRegistry?)null);
        _repository.Setup(x => x.CreateTenantRegistryAsync(It.IsAny<TenantRegistry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRegistry tenant, CancellationToken _) => tenant);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<TenantInfo>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cache.Setup(x => x.RemoveAsync("tenant:list", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.RegisterAsync(
            "tenant-1",
            "Tenant One",
            "realm-1",
            "https://erp.example.com",
            CancellationToken.None);

        Assert.True(result.Registered);
        Assert.Null(result.Message);
        Assert.NotNull(result.Tenant);
        Assert.Equal("tenant-1", result.Tenant.TenantId);
        Assert.Equal("Tenant One", result.Tenant.Name);
        Assert.Equal("realm-1", result.Tenant.KeycloakRealm);
        Assert.Equal("https://erp.example.com", result.Tenant.ErpNextBaseUrl);
        Assert.Equal("active", result.Tenant.HealthStatus);
        Assert.True(result.Tenant.IsActive);

        _repository.Verify(x => x.CreateTenantRegistryAsync(
            It.Is<TenantRegistry>(tenant =>
                tenant.TenantId == "tenant-1" &&
                tenant.SiteName == "Tenant One" &&
                tenant.ErpNextHost == "https://erp.example.com" &&
                tenant.HealthStatus == "active" &&
                tenant.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(x => x.SetAsync(
            "tenant:tenant-1",
            It.Is<TenantInfo>(tenant => tenant.TenantId == "tenant-1"),
            TimeSpan.FromMinutes(5),
            It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(x => x.RemoveAsync("tenant:list", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenTenantAlreadyExists_ReturnsExistingTenant()
    {
        var existing = CreateTenant("tenant-1", "Existing Tenant", "https://existing.example.com");
        _repository.Setup(x => x.GetTenantRegistryAsync("tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<TenantInfo>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.RegisterAsync(
            "tenant-1",
            "Ignored Name",
            "realm-1",
            "https://ignored.example.com",
            CancellationToken.None);

        Assert.False(result.Registered);
        Assert.Equal("Tenant 'tenant-1' already exists.", result.Message);
        Assert.NotNull(result.Tenant);
        Assert.Equal("Existing Tenant", result.Tenant.Name);
        Assert.Equal("realm-1", result.Tenant.KeycloakRealm);
        Assert.Equal("https://existing.example.com", result.Tenant.ErpNextBaseUrl);

        _repository.Verify(x => x.CreateTenantRegistryAsync(It.IsAny<TenantRegistry>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(x => x.SetAsync(
            "tenant:tenant-1",
            It.Is<TenantInfo>(tenant => tenant.TenantId == "tenant-1"),
            TimeSpan.FromMinutes(5),
            It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_WhenCacheHit_ReturnsTenantFromCache()
    {
        var cached = new TenantInfo("tenant-1", "Cached Tenant", "tenant-1", "https://cached.example.com", "active", true);
        _cache.Setup(x => x.GetAsync<TenantInfo>("tenant:tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var service = CreateService();

        var result = await service.GetAsync("tenant-1", CancellationToken.None);

        Assert.Same(cached, result);
        _repository.Verify(x => x.GetTenantRegistryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_WhenCacheMiss_ReturnsTenantFromRepository()
    {
        var tenant = CreateTenant("tenant-1", "Tenant One", "https://erp.example.com");
        _cache.Setup(x => x.GetAsync<TenantInfo>("tenant:tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInfo?)null);
        _repository.Setup(x => x.GetTenantRegistryAsync("tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<TenantInfo>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.GetAsync("tenant-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("tenant-1", result.TenantId);
        Assert.Equal("Tenant One", result.Name);
        Assert.Equal("tenant-1", result.KeycloakRealm);
        Assert.Equal("https://erp.example.com", result.ErpNextBaseUrl);

        _cache.Verify(x => x.SetAsync(
            "tenant:tenant-1",
            It.Is<TenantInfo>(info => info.TenantId == "tenant-1"),
            TimeSpan.FromMinutes(5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenTenantNotFound_ReturnsNull()
    {
        _cache.Setup(x => x.GetAsync<TenantInfo>("tenant:missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInfo?)null);
        _repository.Setup(x => x.GetTenantRegistryAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantRegistry?)null);

        var service = CreateService();

        var result = await service.GetAsync("missing", CancellationToken.None);

        Assert.Null(result);
        _cache.Verify(x => x.SetAsync(
            It.IsAny<string>(),
            It.IsAny<TenantInfo>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_WhenCacheHit_ReturnsCachedList()
    {
        IReadOnlyList<TenantInfo> cached =
        [
            new("tenant-1", "Cached Tenant", "tenant-1", "https://cached.example.com", "active", true)
        ];
        _cache.Setup(x => x.GetAsync<IReadOnlyList<TenantInfo>>("tenant:list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var service = CreateService();

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Same(cached, result);
        _repository.Verify(x => x.ListTenantRegistriesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_WhenCacheMiss_FallsBackToRepository()
    {
        _cache.Setup(x => x.GetAsync<IReadOnlyList<TenantInfo>>("tenant:list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TenantInfo>?)null);
        _repository.Setup(x => x.ListTenantRegistriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateTenant("tenant-1", "Tenant One", "https://one.example.com"),
                CreateTenant("tenant-2", "Tenant Two", "https://two.example.com", "degraded", isActive: true)
            ]);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<List<TenantInfo>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(["tenant-1", "tenant-2"], result.Select(x => x.TenantId));
        Assert.Equal("tenant-1", result[0].KeycloakRealm);
        Assert.Equal("degraded", result[1].HealthStatus);

        _cache.Verify(x => x.SetAsync(
            "tenant:list",
            It.Is<List<TenantInfo>>(tenants => tenants.Count == 2),
            TimeSpan.FromMinutes(5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HealthCheckAsync_WhenTenantActive_ReturnsHealthy()
    {
        _repository.Setup(x => x.GetTenantRegistryAsync("tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTenant("tenant-1", "Tenant One", "https://erp.example.com"));
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<TenantHealthStatus>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.HealthCheckAsync("tenant-1", CancellationToken.None);

        Assert.Equal("tenant-1", result.TenantId);
        Assert.Equal("active", result.Status);
        Assert.True(result.IsHealthy);
        Assert.True(result.CheckedAt <= DateTimeOffset.UtcNow);

        _cache.Verify(x => x.SetAsync(
            "tenant:tenant-1:health",
            It.Is<TenantHealthStatus>(status => status.TenantId == "tenant-1" && status.IsHealthy),
            TimeSpan.FromMinutes(5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HealthCheckAsync_WhenTenantInactive_ReturnsUnhealthy()
    {
        _repository.Setup(x => x.GetTenantRegistryAsync("tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTenant("tenant-1", "Tenant One", "https://erp.example.com", isActive: false));
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<TenantHealthStatus>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var result = await service.HealthCheckAsync("tenant-1", CancellationToken.None);

        Assert.Equal("tenant-1", result.TenantId);
        Assert.Equal("active", result.Status);
        Assert.False(result.IsHealthy);

        _cache.Verify(x => x.SetAsync(
            "tenant:tenant-1:health",
            It.Is<TenantHealthStatus>(status => status.TenantId == "tenant-1" && !status.IsHealthy),
            TimeSpan.FromMinutes(5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TenantRegistry CreateTenant(
        string tenantId,
        string name,
        string erpNextHost,
        string healthStatus = "active",
        bool isActive = true) => new()
        {
            TenantId = tenantId,
            SiteName = name,
            ErpNextHost = erpNextHost,
            HealthStatus = healthStatus,
            IsActive = isActive
        };
}
