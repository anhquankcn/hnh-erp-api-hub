using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Ingestion;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class DlqManagementServiceTests
{
    private const string DlqCacheKey = "erphub:dlq:messages";

    private readonly Mock<IMessageBus> _messageBus = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<DlqManagementService>> _logger = new();

    private DlqManagementService CreateService() => new(
        _messageBus.Object,
        _cache.Object,
        _logger.Object);

    [Fact]
    public async Task GetDeadLettersAsync_ReturnsPaginatedItemsWithCorrectTotalSortedByFailedAtDescending()
    {
        var messages = new List<DlqMessage>
        {
            CreateMessage("oldest", failedAt: new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            CreateMessage("newest", failedAt: new DateTimeOffset(2026, 1, 3, 10, 0, 0, TimeSpan.Zero)),
            CreateMessage("middle", failedAt: new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero))
        };

        _cache.Setup(x => x.GetAsync<List<DlqMessage>>(DlqCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);

        var service = CreateService();

        var result = await service.GetDeadLettersAsync(1, 2, CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(["newest", "middle"], result.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task GetDeadLettersAsync_WhenNoMessages_ReturnsEmptyList()
    {
        _cache.Setup(x => x.GetAsync<List<DlqMessage>>(DlqCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<DlqMessage>?)null);

        var service = CreateService();

        var result = await service.GetDeadLettersAsync(1, 10, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task ReplayAsync_PublishesMessageToCorrectRoutingKey()
    {
        var message = CreateMessage("message-1", routingKey: "erp.customer.created", payload: "{\"name\":\"CUST-001\"}");

        _cache.Setup(x => x.GetAsync<List<DlqMessage>>(DlqCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        _messageBus.Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.ReplayAsync("message-1", CancellationToken.None);

        _messageBus.Verify(x => x.PublishAsync(
            string.Empty,
            "erp.customer.created",
            "{\"name\":\"CUST-001\"}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplayAsync_WhenMessageNotFound_LogsWarning()
    {
        _cache.Setup(x => x.GetAsync<List<DlqMessage>>(DlqCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateMessage("message-1")]);

        var service = CreateService();

        await service.ReplayAsync("missing-message", CancellationToken.None);

        _messageBus.Verify(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        VerifyLog(LogLevel.Warning, "not found for replay", Times.Once());
    }

    [Fact]
    public async Task PurgeAsync_RemovesMessageFromCacheAndUpdatesRemainingMessages()
    {
        var messages = new List<DlqMessage>
        {
            CreateMessage("message-1"),
            CreateMessage("message-2"),
            CreateMessage("message-3")
        };

        _cache.Setup(x => x.GetAsync<List<DlqMessage>>(DlqCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<List<DlqMessage>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.PurgeAsync("message-2", CancellationToken.None);

        _cache.Verify(x => x.SetAsync(
            DlqCacheKey,
            It.Is<List<DlqMessage>>(remaining =>
                remaining.Count == 2 &&
                remaining.Select(x => x.Id).SequenceEqual(new[] { "message-1", "message-3" })),
            TimeSpan.FromDays(7),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurgeAsync_WhenMessageNotFound_DoesNothing()
    {
        _cache.Setup(x => x.GetAsync<List<DlqMessage>>(DlqCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateMessage("message-1")]);

        var service = CreateService();

        await service.PurgeAsync("missing-message", CancellationToken.None);

        _cache.Verify(x => x.SetAsync(
            It.IsAny<string>(),
            It.IsAny<List<DlqMessage>>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DlqMessage CreateMessage(
        string id,
        string routingKey = "erp.customer.updated",
        string payload = "{}",
        string error = "failed",
        DateTimeOffset? failedAt = null)
    {
        return new DlqMessage
        {
            Id = id,
            RoutingKey = routingKey,
            Payload = payload,
            Error = error,
            FailedAt = failedAt ?? DateTimeOffset.UtcNow
        };
    }

    private void VerifyLog(LogLevel level, string messageFragment, Times times)
    {
        _logger.Verify(x => x.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains(messageFragment)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
    }
}
