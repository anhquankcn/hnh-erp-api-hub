using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ERPApiHub.Application.Webhooks;

/// <summary>
/// Receives events from ERPNext Server Scripts, validates HMAC signature,
/// and publishes to RabbitMQ for webhook dispatch.
/// FRD refs: FR-WHK-001b, FR-WHK-002, §16.8
/// </summary>
public sealed class ErpNextEventIngestionService
{
    private readonly ErpHubDbContext _dbContext;
    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ErpNextEventOptions _eventOptions;
    private readonly ILogger<ErpNextEventIngestionService> _logger;

    public ErpNextEventIngestionService(
        ErpHubDbContext dbContext,
        IRabbitMqConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        IOptions<ErpNextEventOptions> eventOptions,
        ILogger<ErpNextEventIngestionService> logger)
    {
        _dbContext = dbContext;
        _connectionFactory = connectionFactory;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _eventOptions = eventOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Validate HMAC-SHA256 signature from ERPNext Server Script.
    /// </summary>
    public bool ValidateSignature(string payload, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_eventOptions.SharedSecret))
        {
            _logger.LogWarning("ERPNext event shared secret not configured. Skipping signature validation.");
            return true;
        }

        // Expected format: "sha256={hex_digest}"
        if (!signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedHex = signatureHeader["sha256=".Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_eventOptions.SharedSecret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant()));
    }

    /// <summary>
    /// Validate event envelope structure.
    /// </summary>
    public (bool IsValid, string? Error) ValidateEnvelope(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("eventId", out var eventIdElem) ||
            eventIdElem.ValueKind != JsonValueKind.String)
        {
            return (false, "eventId is required and must be a string.");
        }

        var eventId = eventIdElem.GetString()!;
        if (eventId.Length != 26)
        {
            return (false, "eventId must be a 26-character ULID.");
        }

        if (!envelope.TryGetProperty("eventType", out var eventTypeElem) ||
            eventTypeElem.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(eventTypeElem.GetString()))
        {
            return (false, "eventType is required.");
        }

        if (!envelope.TryGetProperty("source", out var sourceElem) ||
            sourceElem.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(sourceElem.GetString()))
        {
            return (false, "source is required.");
        }

        if (!envelope.TryGetProperty("correlationId", out var correlationIdElem) ||
            correlationIdElem.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(correlationIdElem.GetString()))
        {
            return (false, "correlationId is required.");
        }

        if (!envelope.TryGetProperty("timestamp", out var timestampElem) ||
            !timestampElem.TryGetDateTimeOffset(out var _))
        {
            return (false, "timestamp must be a valid ISO-8601 timestamp.");
        }

        if (!envelope.TryGetProperty("payload", out _))
        {
            return (false, "payload is required.");
        }

        return (true, null);
    }

    /// <summary>
    /// Process a validated event: publish to RabbitMQ for webhook dispatch.
    /// </summary>
    public async Task ProcessEventAsync(JsonElement envelope, CancellationToken ct)
    {
        var eventType = envelope.GetProperty("eventType").GetString()!;
        var eventId = envelope.GetProperty("eventId").GetString()!;
        var source = envelope.GetProperty("source").GetString()!;
        var correlationId = envelope.GetProperty("correlationId").GetString()!;

        var routingKey = $"erphub.webhook.{eventType.Replace('_', '.')}";

        _logger.LogInformation(
            "Processing ERPNext event {EventId} type {EventType} from {Source}",
            eventId, eventType, source);

        // Publish to RabbitMQ
        var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: _rabbitMqOptions.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // Declare webhook delivery queue
        await channel.QueueDeclareAsync(
            queue: "erphub.webhook.delivery",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        // Bind to webhook routing keys
        await channel.QueueBindAsync(
            queue: "erphub.webhook.delivery",
            exchange: _rabbitMqOptions.ExchangeName,
            routingKey: "erphub.webhook.#",
            cancellationToken: ct);

        var body = Encoding.UTF8.GetBytes(envelope.GetRawText());
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = 2, // persistent
            MessageId = eventId,
            CorrelationId = correlationId
        };

        await channel.BasicPublishAsync(
            exchange: _rabbitMqOptions.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);

        // Write audit log
        var auditLog = new Domain.Entities.AuditLog
        {
            LogId = NetUlid.Ulid.NewUlid().ToString(),
            RequestId = correlationId,
            TenantId = source,
            UserId = "erpnext-server-script",
            Method = "POST",
            Endpoint = "/internal/v1/events/ingest",
            StatusCode = 202,
            DurationMs = 0,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Event {EventId} published to {Exchange} with routing key {RoutingKey}",
            eventId, _rabbitMqOptions.ExchangeName, routingKey);
    }
}