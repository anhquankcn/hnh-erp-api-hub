using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Webhooks;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ERPApiHub.Worker.Consumers;

/// <summary>
/// Consumes events from erphub.webhook.delivery queue, matches subscriptions,
/// and dispatches webhooks with HMAC-SHA256 signing.
/// FRD refs: FR-WHK-002, FR-WHK-003, FR-WHK-004, FR-WHK-005
/// </summary>
public sealed class WebhookDispatcherConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ILogger<WebhookDispatcherConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public WebhookDispatcherConsumer(
        IServiceScopeFactory scopeFactory,
        IRabbitMqConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<WebhookDispatcherConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionFactory = connectionFactory;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection = await _connectionFactory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            exchange: _rabbitMqOptions.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Declare webhook delivery queue with DLQ
        await _channel.QueueDeclareAsync(
            queue: "erphub.webhook.dlq",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        var queueArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = "erphub.webhook.dlq"
        };

        await _channel.QueueDeclareAsync(
            queue: "erphub.webhook.delivery",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: "erphub.webhook.delivery",
            exchange: _rabbitMqOptions.ExchangeName,
            routingKey: "erphub.webhook.#",
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 4, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, args) => await ProcessDeliveryAsync(args, stoppingToken);

        await _channel.BasicConsumeAsync(
            queue: "erphub.webhook.delivery",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Webhook dispatcher consuming from {Queue} bound to {Exchange} with key erphub.webhook.#",
            "erphub.webhook.delivery", _rabbitMqOptions.ExchangeName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken);
            await _connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessDeliveryAsync(BasicDeliverEventArgs args, CancellationToken ct)
    {
        if (_channel is null) return;

        JsonElement envelope;
        try
        {
            using var doc = JsonDocument.Parse(args.Body.Span);
            envelope = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid webhook event envelope. Dead-lettering.");
            await _channel.BasicNackAsync(args.DeliveryTag, false, false, ct);
            return;
        }

        var eventType = envelope.TryGetProperty("eventType", out var etElem)
            ? etElem.GetString() ?? "unknown"
            : "unknown";
        var eventId = envelope.TryGetProperty("eventId", out var eidElem)
            ? eidElem.GetString() ?? "unknown"
            : "unknown";

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ErpHubDbContext>();
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var dataProtection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

            // Find matching subscriptions
            var subscriptions = await dbContext.WebhookSubscriptions
                .Where(s => s.IsActive && s.DeletedAt == null)
                .Where(s => s.EventTypes.Contains(eventType))
                .ToListAsync(ct);

            if (subscriptions.Count == 0)
            {
                _logger.LogDebug("No matching subscriptions for event type {EventType}", eventType);
                await _channel.BasicAckAsync(args.DeliveryTag, false, ct);
                return;
            }

            var payload = envelope.GetRawText();
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            var allDelivered = true;
            foreach (var subscription in subscriptions)
            {
                var delivered = await DispatchWebhookAsync(
                    dbContext,
                    httpClientFactory,
                    dataProtection,
                    subscription,
                    eventType,
                    eventId,
                    payload,
                    payloadBytes,
                    ct);

                allDelivered &= delivered;
            }

            await dbContext.SaveChangesAsync(ct);

            if (allDelivered)
            {
                await _channel.BasicAckAsync(args.DeliveryTag, false, ct);
                return;
            }

            _logger.LogWarning("Webhook event {EventId} delivery failed. Dead-lettering.", eventId);
            await _channel.BasicNackAsync(args.DeliveryTag, false, false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process webhook event {EventId}. Dead-lettering.", eventId);
            await _channel.BasicNackAsync(args.DeliveryTag, false, false, ct);
        }
    }

    private async Task<bool> DispatchWebhookAsync(
        ErpHubDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        WebhookSubscription subscription,
        string eventType,
        string eventId,
        string payloadString,
        byte[] payloadBytes,
        CancellationToken ct)
    {
        var deliveryId = NetUlid.Ulid.NewUlid().ToString();
        var now = DateTime.UtcNow;

        var delivery = new WebhookDelivery
        {
            DeliveryId = deliveryId,
            SubscriptionId = subscription.SubscriptionId,
            EventType = eventType,
            Payload = payloadString,
            StatusCode = 0,
            ResponseBody = null,
            AttemptCount = 0,
            NextRetryAt = null,
            DeliveredAt = null,
            CreatedAt = now
        };

        // Compute HMAC signature
        string? signature = null;
        if (subscription.SecretEncrypted is not null && subscription.SecretEncrypted.Length > 0)
        {
            try
            {
                var protector = dataProtectionProvider.CreateProtector("WebhookSecret");
                var secret = Encoding.UTF8.GetString(protector.Unprotect(subscription.SecretEncrypted));

                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var hash = hmac.ComputeHash(payloadBytes);
                signature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt webhook secret for subscription {SubId}", subscription.SubscriptionId);
            }
        }

        try
        {
            var client = httpClientFactory.CreateClient("WebhookDelivery");
            client.Timeout = TimeSpan.FromSeconds(10);

            var content = new ByteArrayContent(payloadBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (signature is not null)
            {
                content.Headers.Add("X-ERP-Hub-Signature-256", signature);
            }

            content.Headers.Add("X-ERP-Hub-Event-Id", eventId);
            content.Headers.Add("X-ERP-Hub-Event-Type", eventType);

            var response = await client.PostAsync(subscription.WebhookUrl, content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            delivery.StatusCode = (int)response.StatusCode;
            delivery.ResponseBody = responseBody.Length > 2000 ? responseBody[..2000] : responseBody;
            delivery.AttemptCount = 1;
            delivery.NextRetryAt = null;

            if (response.IsSuccessStatusCode)
            {
                delivery.DeliveredAt = now;
                dbContext.WebhookDeliveries.Add(delivery);

                _logger.LogInformation(
                    "Webhook delivered to {Url} for event {EventType}. Status: {StatusCode}",
                    subscription.WebhookUrl, eventType, (int)response.StatusCode);
                return true;
            }

            dbContext.WebhookDeliveries.Add(delivery);

            _logger.LogWarning(
                "Webhook delivery to {Url} failed with {StatusCode}. Dead-lettering message.",
                subscription.WebhookUrl, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            delivery.AttemptCount = 1;
            delivery.NextRetryAt = null;
            delivery.ResponseBody = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            dbContext.WebhookDeliveries.Add(delivery);

            _logger.LogError(ex, "Webhook delivery to {Url} failed. Dead-lettering message.", subscription.WebhookUrl);
            return false;
        }
    }
}
