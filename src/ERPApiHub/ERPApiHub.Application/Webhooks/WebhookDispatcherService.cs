using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.Webhooks;

public sealed class WebhookDispatcherService
{
    private const int MaxStoredResponseLength = 2000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IErpHubRepository _repository;
    private readonly ICacheService _cacheService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookSignatureService _signatureService;
    private readonly WebhookEndpointValidator _endpointValidator;
    private readonly IDataProtector _dataProtector;
    private readonly WebhookDispatcherOptions _options;
    private readonly ILogger<WebhookDispatcherService> _logger;

    public WebhookDispatcherService(
        IErpHubRepository repository,
        ICacheService cacheService,
        IHttpClientFactory httpClientFactory,
        WebhookSignatureService signatureService,
        WebhookEndpointValidator endpointValidator,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<WebhookDispatcherOptions> options,
        ILogger<WebhookDispatcherService> logger)
    {
        _repository = repository;
        _cacheService = cacheService;
        _httpClientFactory = httpClientFactory;
        _signatureService = signatureService;
        _endpointValidator = endpointValidator;
        _dataProtector = dataProtectionProvider.CreateProtector("WebhookSecret");
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WebhookDispatchResult> DispatchRawEventAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();
        return await DispatchEventAsync(root, cancellationToken);
    }

    public async Task<WebhookDispatchResult> DispatchEventAsync(
        JsonElement envelope,
        CancellationToken cancellationToken = default)
    {
        var eventId = GetRequiredString(envelope, "eventId");
        var eventType = GetRequiredString(envelope, "eventType");
        var payload = envelope.GetRawText();

        var subscriptions = await _repository.GetMatchingWebhookSubscriptionsAsync(eventType, cancellationToken);
        if (subscriptions.Count == 0)
        {
            _logger.LogDebug("No webhook subscriptions matched event type {EventType}", eventType);
            return new WebhookDispatchResult(eventId, eventType, 0, 0, 0, 0);
        }

        var delivered = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var subscription in subscriptions)
        {
            if (!await TryClaimDeliveryAsync(eventId, subscription.SubscriptionId, cancellationToken))
            {
                skipped++;
                continue;
            }

            if (await DispatchToSubscriptionAsync(subscription, eventId, eventType, payload, cancellationToken))
            {
                delivered++;
            }
            else
            {
                failed++;
            }
        }

        return new WebhookDispatchResult(eventId, eventType, subscriptions.Count, delivered, failed, skipped);
    }

    private async Task<bool> TryClaimDeliveryAsync(
        string eventId,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var key = $"webhooks:dedup:{eventId}:{subscriptionId}";
        return await _cacheService.TrySetAsync(
            key,
            true,
            TimeSpan.FromHours(Math.Max(1, _options.DedupTtlHours)),
            cancellationToken);
    }

    private async Task<bool> DispatchToSubscriptionAsync(
        WebhookSubscription subscription,
        string eventId,
        string eventType,
        string payload,
        CancellationToken cancellationToken)
    {
        var deliveryId = UlidGenerator.Generate();
        var delivery = new WebhookDelivery
        {
            DeliveryId = deliveryId,
            SubscriptionId = subscription.SubscriptionId,
            EventType = eventType,
            Payload = payload,
            Status = "pending",
            AttemptCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptedAt = DateTimeOffset.UtcNow
        };

        var endpointValidation = await _endpointValidator.ValidateAsync(subscription.WebhookUrl, cancellationToken);
        if (!endpointValidation.IsValid)
        {
            delivery.Status = "blocked";
            delivery.ResponseBody = endpointValidation.Error;
            await _repository.CreateWebhookDeliveryAsync(delivery, cancellationToken);
            _logger.LogWarning(
                "Blocked webhook delivery {DeliveryId} for subscription {SubscriptionId}: {Reason}",
                deliveryId,
                subscription.SubscriptionId,
                endpointValidation.Error);
            return false;
        }

        var secret = TryGetSecret(subscription);
        var maxAttempts = Math.Max(1, _options.MaxAttempts);
        var initialBackoff = TimeSpan.FromSeconds(Math.Max(1, _options.InitialBackoffSeconds));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            delivery.AttemptCount = attempt;
            delivery.AttemptedAt = DateTimeOffset.UtcNow;

            try
            {
                var response = await SendAsync(subscription, eventId, eventType, deliveryId, payload, secret, cancellationToken);
                delivery.HttpStatus = (int)response.StatusCode;
                delivery.ResponseBody = Truncate(await response.Content.ReadAsStringAsync(cancellationToken));

                if (response.IsSuccessStatusCode)
                {
                    delivery.Status = "delivered";
                    delivery.DeliveredAt = DateTimeOffset.UtcNow;
                    delivery.NextRetryAt = null;
                    await _repository.CreateWebhookDeliveryAsync(delivery, cancellationToken);
                    return true;
                }

                delivery.Status = "retrying";
                _logger.LogWarning(
                    "Webhook delivery {DeliveryId} attempt {Attempt}/{MaxAttempts} failed with HTTP {StatusCode}",
                    deliveryId,
                    attempt,
                    maxAttempts,
                    (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                delivery.Status = "retrying";
                delivery.ResponseBody = Truncate(ex.Message);
                _logger.LogWarning(
                    ex,
                    "Webhook delivery {DeliveryId} attempt {Attempt}/{MaxAttempts} failed",
                    deliveryId,
                    attempt,
                    maxAttempts);
            }

            if (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromTicks(initialBackoff.Ticks * (1L << (attempt - 1)));
                delivery.NextRetryAt = DateTimeOffset.UtcNow + delay;
                await Task.Delay(delay, cancellationToken);
            }
        }

        delivery.Status = "failed";
        delivery.NextRetryAt = null;
        await _repository.CreateWebhookDeliveryAsync(delivery, cancellationToken);
        return false;
    }

    private async Task<HttpResponseMessage> SendAsync(
        WebhookSubscription subscription,
        string eventId,
        string eventType,
        string deliveryId,
        string payload,
        byte[]? secret,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("WebhookDelivery");
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.WebhookUrl);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.Add("X-ERP-Hub-Event-Id", eventId);
        request.Headers.Add("X-ERP-Hub-Event-Type", eventType);
        request.Headers.Add("X-ERP-Hub-Delivery-Id", deliveryId);

        var timestamp = DateTimeOffset.UtcNow;
        request.Headers.Add("X-ERP-Hub-Timestamp", timestamp.ToUnixTimeSeconds().ToString());

        if (secret is not null)
        {
            request.Headers.Add(
                "X-ERP-Hub-Signature-256",
                _signatureService.ComputeDeliverySignature(payload, secret, timestamp, deliveryId));
        }

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.SendAsync(request, cancellationToken);
    }

    private byte[]? TryGetSecret(WebhookSubscription subscription)
    {
        if (subscription.SecretEncrypted is null || subscription.SecretEncrypted.Length == 0)
        {
            return null;
        }

        try
        {
            return _dataProtector.Unprotect(subscription.SecretEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt webhook secret for subscription {SubscriptionId}", subscription.SubscriptionId);
            return null;
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!;
        }

        throw new JsonException($"Webhook event envelope must include {propertyName}.");
    }

    private static string Truncate(string? value) =>
        value is null
            ? string.Empty
            : value.Length <= MaxStoredResponseLength
                ? value
                : value[..MaxStoredResponseLength];
}
