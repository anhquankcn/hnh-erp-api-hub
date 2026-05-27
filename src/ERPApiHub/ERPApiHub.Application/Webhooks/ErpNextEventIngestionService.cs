using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.Webhooks;

/// <summary>
/// Receives events from ERPNext Server Scripts, validates HMAC signature,
/// and publishes to RabbitMQ for webhook dispatch.
/// FRD refs: FR-WHK-001b, FR-WHK-002, §16.8
/// </summary>
public sealed class ErpNextEventIngestionService
{
    private readonly IErpHubRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ErpNextEventOptions _eventOptions;
    private readonly ILogger<ErpNextEventIngestionService> _logger;

    public ErpNextEventIngestionService(
        IErpHubRepository repository,
        IMessageBus messageBus,
        IOptions<ErpNextEventOptions> eventOptions,
        ILogger<ErpNextEventIngestionService> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
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
            _logger.LogCritical("ERPNext event shared secret not configured! Signature validation is DISABLED.");

            if (_eventOptions.SkipSignatureValidation)
            {
                _logger.LogWarning(
                    "Signature validation explicitly skipped due to SkipSignatureValidation=true");
                return true;
            }

            return false;
        }

        var secretBytes = Encoding.UTF8.GetBytes(_eventOptions.SharedSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var expectedSignature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signatureHeader));
    }

    /// <summary>
    /// Process validated event: store and publish to message bus.
    /// </summary>
    public async Task<IngestEventResult> IngestEventAsync(
        string eventType,
        string payload,
        string? signatureHeader,
        CancellationToken ct)
    {
        // Validate signature
        if (!string.IsNullOrWhiteSpace(signatureHeader) && !ValidateSignature(payload, signatureHeader))
        {
            _logger.LogWarning("Invalid HMAC signature for event type {EventType}", eventType);
            return new IngestEventResult(false, "Invalid signature", null);
        }

        // Parse payload
        JsonElement data;
        try
        {
            data = JsonSerializer.Deserialize<JsonElement>(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse event payload");
            return new IngestEventResult(false, "Invalid JSON payload", null);
        }

        // Create processed event record
        var processedEventId = UlidGenerator.Generate();
        var processedEvent = new ErpProcessedEvent
        {
            ErpProcessedEventId = processedEventId,
            Source = "erpnext-webhook",
            EventType = eventType,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateProcessedEventAsync(processedEvent, ct);

        // Publish to message bus for webhook dispatch
        var envelope = new ErpEventEnvelope(
            EventId: processedEventId,
            EventType: eventType,
            Timestamp: DateTimeOffset.UtcNow,
            Payload: data,
            CorrelationId: null);

        await _messageBus.PublishAsync(
            "erphub.events",
            $"event.{eventType}",
            envelope,
            ct);

        _logger.LogInformation(
            "ERPNext event {EventId} of type {EventType} ingested and published",
            processedEventId, eventType);

        return new IngestEventResult(true, null, processedEventId);
    }
}

public sealed record IngestEventResult(bool Success, string? Error, string? EventId);
public sealed record ErpEventEnvelope(
    string EventId,
    string EventType,
    DateTimeOffset Timestamp,
    JsonElement Payload,
    string? CorrelationId);
