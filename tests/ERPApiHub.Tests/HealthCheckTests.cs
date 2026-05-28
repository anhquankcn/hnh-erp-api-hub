using System.Net;
using ERPApiHub.API.HealthChecks;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.ErpNext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task DbContextHealthCheck_WhenDatabaseConnects_ReturnsHealthy()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var healthCheck = new DbContextHealthCheck(dbContext);

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task DbContextHealthCheck_WhenDatabaseFails_ReturnsUnhealthy()
    {
        var options = new DbContextOptionsBuilder<ErpHubDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=erphub;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1")
            .Options;
        await using var dbContext = new ErpHubDbContext(options);
        var healthCheck = new DbContextHealthCheck(dbContext);

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task RedisHealthCheck_WhenPingSucceeds_ReturnsHealthy()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var healthCheck = new RedisHealthCheck(cache.Object);

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task RedisHealthCheck_WhenPingFails_ReturnsUnhealthy()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable."));
        var healthCheck = new RedisHealthCheck(cache.Object);

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task RabbitMqHealthCheck_WhenConnected_ReturnsHealthy()
    {
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var healthCheck = new RabbitMqHealthCheck(messageBus.Object);

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task RabbitMqHealthCheck_WhenDisconnected_ReturnsUnhealthy()
    {
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var healthCheck = new RabbitMqHealthCheck(messageBus.Object);

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task ErpNextHealthCheck_WhenPingSucceeds_ReturnsHealthy()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK));
        var healthCheck = new ErpNextHealthCheck(
            httpClient,
            Options.Create(new ErpNextOptions { BaseUrl = "https://erpnext.example.test" }));

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ErpNextHealthCheck_WhenPingFails_ReturnsUnhealthy()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable));
        var healthCheck = new ErpNextHealthCheck(
            httpClient,
            Options.Create(new ErpNextOptions { BaseUrl = "https://erpnext.example.test" }));

        var result = await healthCheck.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private static ErpHubDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ErpHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ErpHubDbContext(options);
    }

    private static HealthCheckContext CreateContext() => new()
    {
        Registration = new HealthCheckRegistration("test", _ => throw new NotSupportedException(), null, [])
    };

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            Assert.Equal("/api/method/ping", request.RequestUri?.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
