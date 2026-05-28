using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Query;
using ERPApiHub.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class QueryServiceTests
{
    private readonly Mock<IErpNextClient> _erpNextClient = new();
    private readonly Mock<ICacheService> _cache = new();
    private Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly Mock<ILogger<QueryService>> _logger = new();
    private readonly ErpHubDbContext _dbContext;
    private readonly IErpHubRepository _repository;

    public QueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<ErpHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ErpHubDbContext(options);
        _repository = new ErpHubRepository(_dbContext);

        SetupHttpContext("SGN");
    }

    private void SetupHttpContext(string branchId, bool noCache = false)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("BranchId", branchId),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, "user-1")
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        if (noCache)
        {
            httpContext.Request.Headers.CacheControl = "no-cache";
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);
        _httpContextAccessor = accessor;
    }

    private QueryService CreateService() => new(
        _erpNextClient.Object,
        _cache.Object,
        _repository,
        _httpContextAccessor.Object,
        _logger.Object);

    [Fact]
    public async Task ListAsync_WhenCacheHit_ReturnsCachedResult()
    {
        var cached = new PaginatedResponse<JsonElement>
        {
            Data = [],
            Pagination = new PaginationMeta { Page = 1, PageSize = 20, TotalCount = 0 }
        };

        _cache.Setup(x => x.GetAsync<PaginatedResponse<JsonElement>>(
            It.Is<string>(k => k.StartsWith("query:SGN:Customer")), default))
            .ReturnsAsync(cached);

        var service = CreateService();
        var request = new QueryRequest { Doctype = "Customer", Page = 1, PageSize = 20 };

        var result = await service.ListAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        _erpNextClient.Verify(x => x.GetAsync<JsonElement>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _erpNextClient.Verify(x => x.GetAsync<JsonElement>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_WhenCacheMiss_QueriesErpNext()
    {
        _cache.Setup(x => x.GetAsync<PaginatedResponse<JsonElement>>(It.IsAny<string>(), default))
            .ReturnsAsync((PaginatedResponse<JsonElement>?)null);
        _cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), default))
            .Returns(Task.CompletedTask);

        var responseData = JsonDocument.Parse("{\"data\":[{\"name\":\"CUST-001\"}]}").RootElement;
        _erpNextClient.Setup(x => x.GetAsync<JsonElement>(
                It.IsAny<string>(),
                "SGN",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpNextResponse<JsonElement>(responseData, 200, null));

        // Force no-cache to bypass cache lookup
        SetupHttpContext("SGN", noCache: true);

        var service = CreateService();
        var request = new QueryRequest { Doctype = "Customer", Page = 1, PageSize = 20 };

        var result = await service.ListAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Data);
        _erpNextClient.Verify(x => x.GetAsync<JsonElement>(
            It.Is<string>(p => p.StartsWith("Customer?")),
            "SGN",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_WithExplicitTenant_UsesTenantForCacheAndErpNext()
    {
        _cache.Setup(x => x.GetAsync<PaginatedResponse<JsonElement>>(It.IsAny<string>(), default))
            .ReturnsAsync((PaginatedResponse<JsonElement>?)null);
        _cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), default))
            .Returns(Task.CompletedTask);

        var responseData = JsonDocument.Parse("{\"data\":[{\"name\":\"CUST-001\"}]}").RootElement;
        _erpNextClient.Setup(x => x.GetAsync<JsonElement>(
                It.IsAny<string>(),
                "HAN",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpNextResponse<JsonElement>(responseData, 200, null));

        var service = CreateService();
        var request = new QueryRequest { Doctype = "Customer", Page = 1, PageSize = 20 };

        var result = await service.ListAsync(request, "HAN", CancellationToken.None);

        Assert.Single(result.Data);
        _cache.Verify(x => x.GetAsync<PaginatedResponse<JsonElement>>(
            It.Is<string>(k => k.StartsWith("query:HAN:Customer")),
            It.IsAny<CancellationToken>()), Times.Once);
        _erpNextClient.Verify(x => x.GetAsync<JsonElement>(
            It.Is<string>(p => p.StartsWith("Customer?")),
            "HAN",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenNotFound_ThrowsKeyNotFoundException()
    {
        _cache.Setup(x => x.GetAsync<JsonElement?>(It.IsAny<string>(), default))
            .ReturnsAsync((JsonElement?)null);

        _erpNextClient.Setup(x => x.GetAsync<JsonElement>("Customer/CUST-999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpNextResponse<JsonElement>(default, 404, "Not found"));

        SetupHttpContext("SGN", noCache: true);

        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetAsync("Customer", "CUST-999", CancellationToken.None));
    }

    [Fact]
    public async Task CountAsync_ReturnsCountFromErpNext()
    {
        _cache.Setup(x => x.GetAsync<object>(It.IsAny<string>(), default))
            .ReturnsAsync((object?)null);
        _cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), default))
            .Returns(Task.CompletedTask);

        var responseData = JsonDocument.Parse("[{\"count(*)\":42}]").RootElement;
        _erpNextClient.Setup(x => x.GetAsync<JsonElement>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpNextResponse<JsonElement>(responseData, 200, null));

        SetupHttpContext("SGN", noCache: true);

        var service = CreateService();
        var result = await service.CountAsync("Customer", CancellationToken.None);

        var typed = Assert.IsType<dynamic>(result);
        Assert.Equal(42L, (long)typed.count);
    }
}
