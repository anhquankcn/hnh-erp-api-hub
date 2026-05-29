using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Cache;
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
    private readonly CacheInvalidationService _cacheInvalidationService;
    private readonly ErpNextEventOptions _eventOptions;
    private readonly ILogger<ErpNextEventIngestionService> _logger;

    public ErpNextEventIngestionService(
        IErpHubRepository repository,
        IMessageBus messageBus,
        CacheInvalidationService cacheInvalidationService,
        IOptions<ErpNextEventOptions> eventOptions,
        ILogger<ErpNextEventIngestionService> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _cacheInvalidationService = cacheInvalidationService;
        _eventOptions = eventOptions.Value;
        _logger = logger;
    }

    public bool ValidateSignature(string payload, string signatureHeader, string timestampHeader)
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

        if (!TryParseTimestamp(timestampHeader, out var timestamp))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (timestamp > now.AddSeconds(_eventOptions.MaxClockSkewSeconds)
            || timestamp < now.AddSeconds(-_eventOptions.MaxClockSkewSeconds))
        {
            return false;
        }

        var secretBytes = Encoding.UTF8.GetBytes(_eventOptions.SharedSecret);
        var signedPayload = $"{timestamp.ToUnixTimeSeconds()}.{payload}";
        var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var expectedSignature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";

        if (expectedSignature.Length != signatureHeader.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signatureHeader));
    }

    /// <summary>
    /// Backward-compatible validation for older tests and callers.
    /// </summary>
    public bool ValidateSignature(string payload, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_eventOptions.SharedSecret))
        {
            _logger.LogCritical("ERPNext event shared secret not configured! Signature validation is DISABLED.");
            return _eventOptions.SkipSignatureValidation;
        }

        var secretBytes = Encoding.UTF8.GetBytes(_eventOptions.SharedSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var expectedSignature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";

        return expectedSignature.Length == signatureHeader.Length
            && CryptographicOperations.FixedTimeEquals(
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
        string? timestampHeader,
        CancellationToken ct)
    {
        // Validate signature
        if (!string.IsNullOrWhiteSpace(signatureHeader)
            && !ValidateSignature(payload, signatureHeader, timestampHeader ?? string.Empty))
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

        if (!HasRequiredString(data, "eventType")
            || !HasRequiredString(data, "doctype")
            || !HasRequiredString(data, "name")
            || !HasRequiredTimestamp(data, "timestamp"))
        {
            return new IngestEventResult(false, "Payload must include eventType, doctype, name, and timestamp", null);
        }

        var doctype = data.GetProperty("doctype").GetString()!;
        var tenantId = TryGetOptionalString(data, "tenantId")
            ?? TryGetOptionalString(data, "branchId");

        // Create processed event record
        var requestedEventId = data.TryGetProperty("eventId", out var eventIdElement)
            && eventIdElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(eventIdElement.GetString())
                ? eventIdElement.GetString()!
                : null;
        var processedEventId = requestedEventId is { Length: <= 26 } ? requestedEventId : UlidGenerator.Generate();
        var existing = await _repository.GetProcessedEventAsync(processedEventId, ct);
        if (existing is not null)
        {
            _logger.LogInformation("ERPNext event {EventId} already ingested; skipping duplicate publish", processedEventId);
            return new IngestEventResult(true, "Duplicate event", processedEventId);
        }

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
            "1stopshop_event_bus",
            $"erphub.webhook.{eventType}",
            envelope,
            ct);

        await _cacheInvalidationService.InvalidateDoctypeAsync(doctype, tenantId, ct);

        _logger.LogInformation(
            "ERPNext event {EventId} of type {EventType} ingested and published",
            processedEventId, eventType);

        return new IngestEventResult(true, null, processedEventId);
    }

    public Task<IngestEventResult> IngestEventAsync(
        string eventType,
        string payload,
        string? signatureHeader,
        CancellationToken ct) =>
        IngestEventAsync(eventType, payload, signatureHeader, null, ct);

    private static bool TryParseTimestamp(string timestampHeader, out DateTimeOffset timestamp)
    {
        if (long.TryParse(timestampHeader, out var unixSeconds))
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }

        return DateTimeOffset.TryParse(timestampHeader, out timestamp);
    }

    private static bool HasRequiredString(JsonElement data, string propertyName) =>
        data.TryGetProperty(propertyName, out var element)
        && element.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(element.GetString());

    private static bool HasRequiredTimestamp(JsonElement data, string propertyName) =>
        data.TryGetProperty(propertyName, out var element)
        && element.ValueKind == JsonValueKind.String
        && TryParseTimestamp(element.GetString() ?? string.Empty, out _);

    private static string? TryGetOptionalString(JsonElement data, string propertyName) =>
        data.TryGetProperty(propertyName, out var element)
        && element.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;
}

public sealed record IngestEventResult(bool Success, string? Error, string? EventId);
public sealed record ErpEventEnvelope(
    string EventId,
    string EventType,
    DateTimeOffset Timestamp,
    JsonElement Payload,
    string? CorrelationId);
