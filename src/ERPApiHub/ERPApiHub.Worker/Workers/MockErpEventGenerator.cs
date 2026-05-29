using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Webhooks;
using ERPApiHub.Domain;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Worker.Workers;

public sealed class MockErpEventGenerator(
    IMessageBus messageBus,
    IOptions<MockErpEventGeneratorOptions> options,
    ILogger<MockErpEventGenerator> logger) : BackgroundService
{
    private readonly MockErpEventGeneratorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.IntervalSeconds)));
        do
        {
            await PublishMockEventAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PublishMockEventAsync(CancellationToken cancellationToken)
    {
        var eventType = _options.EventTypes.Length == 0
            ? "customer_created"
            : _options.EventTypes[Random.Shared.Next(_options.EventTypes.Length)];

        var payload = new
        {
            eventId = UlidGenerator.Generate(),
            eventType,
            doctype = MapDoctype(eventType),
            name = $"MOCK-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            timestamp = DateTimeOffset.UtcNow,
            tenantId = _options.TenantId,
            source = "mock-erp-event-generator"
        };

        var payloadJson = JsonSerializer.SerializeToElement(payload);
        var envelope = new ErpEventEnvelope(
            payload.eventId,
            eventType,
            DateTimeOffset.UtcNow,
            payloadJson,
            CorrelationId: null);

        await messageBus.PublishAsync(
            "1stopshop_event_bus",
            $"erphub.webhook.{eventType}",
            envelope,
            cancellationToken);

        logger.LogInformation("Published mock ERP event {EventId} ({EventType})", payload.eventId, eventType);
    }

    private static string MapDoctype(string eventType) =>
        eventType switch
        {
            "customer_created" => "Customer",
            "booking_created" => "Booking",
            "invoice_paid" => "Sales Invoice",
            _ => "ERPNext Event"
        };
}

public sealed class MockErpEventGeneratorOptions
{
    public const string SectionName = "MockErpEventGenerator";

    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public string TenantId { get; set; } = "mock";
    public string[] EventTypes { get; set; } = ["customer_created", "booking_created", "invoice_paid"];
}
