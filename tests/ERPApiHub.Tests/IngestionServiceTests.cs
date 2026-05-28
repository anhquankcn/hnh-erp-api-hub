using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Ingestion;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class IngestionServiceTests
{
    private readonly Mock<IAllowedDoctypeValidator> _doctypeValidator = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IMessageBus> _messageBus = new();
    private Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly Mock<ILogger<IngestionService>> _logger = new();

    private readonly ErpHubDbContext _dbContext;
    private readonly IErpHubRepository _repository;

    public IngestionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ErpHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ErpHubDbContext(options);
        _repository = new ErpHubRepository(_dbContext);

        SetupHttpContext("SGN", "user-123");
    }

    private void SetupHttpContext(string branchId, string userId)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("BranchId", branchId),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, userId)
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Headers["X-Request-ID"] = "corr-123";
        httpContext.Request.Headers["X-Idempotency-Key"] = "idem-123";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);
        _httpContextAccessor = accessor;
    }

    private IngestionService CreateService()
    {
        return new IngestionService(
            _doctypeValidator.Object,
            _cache.Object,
            _repository,
            _httpContextAccessor.Object,
            _messageBus.Object,
            _logger.Object);
    }

    [Fact]
    public async Task IngestAsync_WhenDoctypeNotAllowed_ThrowsArgumentException()
    {
        _doctypeValidator.Setup(x => x.IsAllowed("InvalidDoc")).Returns(false);

        var service = CreateService();
        var payload = JsonDocument.Parse("{\"name\":\"test\"}").RootElement;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IngestAsync("InvalidDoc", payload, null, CancellationToken.None));
    }

    [Fact]
    public async Task IngestAsync_WhenIdempotencyKeyExists_ReturnsCachedResponse()
    {
        _doctypeValidator.Setup(x => x.IsAllowed("Customer")).Returns(true);

        var cachedResponse = new IngestionResponse("cached-job-123", "completed", "corr-1");
        _cache.Setup(x => x.GetAsync<IngestionResponse>(
            It.Is<string>(k => k.Contains("idem-123")), default))
            .ReturnsAsync(cachedResponse);

        var service = CreateService();
        var payload = JsonDocument.Parse("{\"name\":\"test\"}").RootElement;

        var result = await service.IngestAsync("Customer", payload, null, CancellationToken.None);

        Assert.Equal("cached-job-123", result.JobId);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task IngestAsync_WhenValid_PublishesToRabbitMq()
    {
        _doctypeValidator.Setup(x => x.IsAllowed("Customer")).Returns(true);
        _cache.Setup(x => x.GetAsync<IngestionResponse>(It.IsAny<string>(), default))
            .ReturnsAsync((IngestionResponse?)null);
        _cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), default))
            .Returns(Task.CompletedTask);

        _messageBus.Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var payload = JsonDocument.Parse("{\"name\":\"test\"}").RootElement;

        // Remove idempotency key for this test
        var httpContext = _httpContextAccessor.Object.HttpContext!;
        httpContext.Request.Headers.Remove("X-Idempotency-Key");

        var result = await service.IngestAsync("Customer", payload, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("pending", result.Status);
        _messageBus.Verify(x => x.PublishAsync(
            string.Empty,
            It.Is<string>(rk => rk.Contains("Customer") && rk.Contains("created")),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IngestAsync_WhenValid_WritesAuditLog()
    {
        _doctypeValidator.Setup(x => x.IsAllowed("Customer")).Returns(true);
        _cache.Setup(x => x.GetAsync<IngestionResponse>(It.IsAny<string>(), default))
            .ReturnsAsync((IngestionResponse?)null);
        _cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan?>(), default))
            .Returns(Task.CompletedTask);

        _messageBus.Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var httpContext = _httpContextAccessor.Object.HttpContext!;
        httpContext.Request.Headers.Remove("X-Idempotency-Key");

        var service = CreateService();
        var payload = JsonDocument.Parse("{\"name\":\"test\"}").RootElement;

        await service.IngestAsync("Customer", payload, null, CancellationToken.None);

        var auditLogs = await _dbContext.AuditLogs.ToListAsync();
        Assert.Single(auditLogs);
        Assert.Equal("SGN", auditLogs[0].TenantId);
        Assert.Equal("POST", auditLogs[0].Method);
        Assert.Equal(202, auditLogs[0].StatusCode);
    }

    [Fact]
    public async Task BatchIngestAsync_WhenOver100Ops_ThrowsArgumentException()
    {
        var service = CreateService();
        var ops = Enumerable.Range(0, 101)
            .Select(_ => new BatchOperation("Customer", JsonDocument.Parse("{}").RootElement))
            .ToList();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.BatchIngestAsync(ops, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WhenDoctypeNotAllowed_ThrowsArgumentException()
    {
        _doctypeValidator.Setup(x => x.IsAllowed("ForbiddenDoc")).Returns(false);

        var service = CreateService();
        var payload = JsonDocument.Parse("{}").RootElement;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync("ForbiddenDoc", "doc-1", CancellationToken.None));
    }
}
