using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Polling;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Messaging;
using ERPApiHub.Worker.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class PollingWorkerTests
{
    [Fact]
    public void DoctypePollingRegistry_ReturnsDefaultPollingSetWithIntervals()
    {
        var options = Options.Create(new PollingOptions());
        var registry = new DoctypePollingRegistry(options);

        var doctypes = registry.GetActiveDoctypes();

        Assert.Equal(5, doctypes.Count);
        Assert.Contains(doctypes, x =>
            x.Doctype == "Sales Invoice"
            && x.Priority == PollingPriority.Critical
            && x.Interval == TimeSpan.FromSeconds(30));
        Assert.Contains(doctypes, x =>
            x.Doctype == "Customer"
            && x.Priority == PollingPriority.Standard
            && x.Interval == TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task RunOnceAsync_PublishesPollingChangesAndUpdatesTenantCursor()
    {
        var repository = new Mock<IErpHubRepository>();
        var cache = new Mock<ICacheService>();
        var erpNextClient = new Mock<IErpNextClient>();
        var messageBus = new Mock<IMessageBus>();
        var options = new PollingOptions
        {
            Doctypes =
            [
                new()
                {
                    Name = "Customer",
                    Priority = PollingPriority.Critical,
                    Interval = TimeSpan.FromSeconds(30),
                    LastCursorField = "modified"
                }
            ]
        };

        repository
            .Setup(x => x.ListTenantRegistriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TenantRegistry
                {
                    TenantId = "tenant-1",
                    HealthStatus = "active",
                    IsActive = true
                },
                new TenantRegistry
                {
                    TenantId = "tenant-2",
                    HealthStatus = "inactive",
                    IsActive = true
                }
            ]);

        cache
            .Setup(x => x.GetAsync<string>("erphub:polling:tenant-1:Customer:cursor", It.IsAny<CancellationToken>()))
            .ReturnsAsync("2026-05-28T00:00:00.0000000+00:00");

        var response = JsonDocument.Parse(
            """
            {
              "data": [
                {
                  "name": "CUST-0001",
                  "modified": "2026-05-28T01:02:03.0000000+00:00"
                }
              ]
            }
            """).RootElement.Clone();

        erpNextClient
            .Setup(x => x.GetAsync<JsonElement>(
                It.Is<string>(path => path.StartsWith("Customer?", StringComparison.Ordinal)),
                "tenant-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpNextResponse<JsonElement>(response, 200, null));

        var worker = CreateWorker(options, repository, cache, erpNextClient, messageBus);

        await worker.RunOnceAsync(CancellationToken.None);

        messageBus.Verify(x => x.PublishAsync(
            "1stopshop_event_bus",
            "erphub.ingestion.Customer.polling",
            It.Is<ErpEventEnvelope>(envelope =>
                envelope.EventType == "erphub.ingestion.Customer.polling"
                && envelope.Source == "ERPApiHub"
                && envelope.CorrelationId == "tenant-1"
                && envelope.Payload.GetProperty("name").GetString() == "CUST-0001"
                && envelope.Payload.GetProperty("tenantId").GetString() == "tenant-1"
                && envelope.Payload.GetProperty("source").GetString() == "polling"),
            It.IsAny<CancellationToken>()), Times.Once);

        cache.Verify(x => x.SetAsync(
            "erphub:polling:tenant-1:Customer:cursor",
            "2026-05-28T01:02:03.0000000+00:00",
            TimeSpan.FromHours(24),
            It.IsAny<CancellationToken>()), Times.Once);

        erpNextClient.Verify(x => x.GetAsync<JsonElement>(
            It.IsAny<string>(),
            "tenant-2",
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotUpdateCursorWhenErpNextRateLimits()
    {
        var repository = new Mock<IErpHubRepository>();
        var cache = new Mock<ICacheService>();
        var erpNextClient = new Mock<IErpNextClient>();
        var messageBus = new Mock<IMessageBus>();
        var options = new PollingOptions
        {
            Doctypes = [new() { Name = "Item", Priority = PollingPriority.Standard }]
        };

        repository
            .Setup(x => x.ListTenantRegistriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TenantRegistry { TenantId = "tenant-1", HealthStatus = "active", IsActive = true }]);

        erpNextClient
            .Setup(x => x.GetAsync<JsonElement>(It.IsAny<string>(), "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpNextResponse<JsonElement>(default, 429, "rate limited"));

        var worker = CreateWorker(options, repository, cache, erpNextClient, messageBus);

        await worker.RunOnceAsync(CancellationToken.None);

        messageBus.Verify(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ErpEventEnvelope>(),
            It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(x => x.SetAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PollingWorker CreateWorker(
        PollingOptions options,
        Mock<IErpHubRepository> repository,
        Mock<ICacheService> cache,
        Mock<IErpNextClient> erpNextClient,
        Mock<IMessageBus> messageBus)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(cache.Object);
        services.AddSingleton(erpNextClient.Object);
        services.AddSingleton(messageBus.Object);

        var provider = services.BuildServiceProvider();
        return new PollingWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DoctypePollingRegistry(Options.Create(options)),
            Options.Create(options),
            Options.Create(new RabbitMqOptions()),
            Mock.Of<ILogger<PollingWorker>>());
    }
}
