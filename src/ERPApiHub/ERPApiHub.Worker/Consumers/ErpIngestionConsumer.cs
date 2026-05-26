using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ERPApiHub.Worker.Consumers;

public sealed class ErpIngestionConsumer(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<ErpIngestionConsumer> logger) : BackgroundService
{
    private const int MaxRetries = 3;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rabbitMq = options.Value;
        _connection = await connectionFactory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            exchange: rabbitMq.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: rabbitMq.IngestionDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = rabbitMq.IngestionDeadLetterQueue
        };

        await _channel.QueueDeclareAsync(
            queue: rabbitMq.IngestionQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: rabbitMq.IngestionQueue,
            exchange: rabbitMq.ExchangeName,
            routingKey: rabbitMq.IngestionBindingKey,
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 8, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, args) => await ProcessDeliveryAsync(args, stoppingToken);

        await _channel.BasicConsumeAsync(
            queue: rabbitMq.IngestionQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "ERP Hub worker consuming {Queue} from {Exchange} with binding {BindingKey}",
            rabbitMq.IngestionQueue,
            rabbitMq.ExchangeName,
            rabbitMq.IngestionBindingKey);

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

    private async Task ProcessDeliveryAsync(BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        ErpEventEnvelope? envelope;
        try
        {
            envelope = DeserializeEnvelope(args.Body.Span);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Invalid ERP ingestion event envelope. Dead-lettering message.");
            await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await ProcessEnvelopeAsync(envelope, cancellationToken);
                await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(
                    ex,
                    "Transient failure processing event {EventId}. Retry {Attempt}/{MaxRetries} after {DelayMs}ms.",
                    envelope.EventId,
                    attempt,
                    MaxRetries,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Event {EventId} failed permanently. Dead-lettering message.", envelope.EventId);
                await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                return;
            }
        }
    }

    private async Task ProcessEnvelopeAsync(ErpEventEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpHubDbContext>();
        var erpNextClient = scope.ServiceProvider.GetRequiredService<IErpNextClient>();

        var alreadyProcessed = await dbContext.ErpProcessedEvents
            .AnyAsync(x => x.ErpProcessedEventId == envelope.EventId, cancellationToken);

        if (alreadyProcessed)
        {
            logger.LogInformation("Skipping duplicate ERP ingestion event {EventId}", envelope.EventId);
            return;
        }

        await erpNextClient.PostAsync<object>("events/ingestion", envelope, cancellationToken);

        dbContext.ErpProcessedEvents.Add(new ErpProcessedEvent
        {
            ErpProcessedEventId = envelope.EventId,
            Source = envelope.Source,
            EventType = envelope.EventType,
            ProcessedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ErpEventEnvelope DeserializeEnvelope(ReadOnlySpan<byte> body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var eventId = ReadRequiredString(root, "eventId");
        var eventType = ReadRequiredString(root, "eventType");
        var source = ReadRequiredString(root, "source");
        var correlationId = ReadRequiredString(root, "correlationId");
        var timestamp = root.TryGetProperty("timestamp", out var timestampElement)
            && timestampElement.TryGetDateTimeOffset(out var parsedTimestamp)
                ? parsedTimestamp
                : throw new InvalidOperationException("timestamp must be an ISO-8601 timestamp.");
        var version = ReadRequiredScalarAsString(root, "version");
        var payload = root.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : throw new InvalidOperationException("payload is required.");

        var envelope = new ErpEventEnvelope(
            eventId,
            eventType,
            source,
            correlationId,
            timestamp,
            version,
            payload);

        if (envelope.EventId.Length != 26)
        {
            throw new InvalidOperationException("eventId must be a 26-character ULID.");
        }

        if (string.IsNullOrWhiteSpace(envelope.EventType)
            || string.IsNullOrWhiteSpace(envelope.Source)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId)
            || string.IsNullOrWhiteSpace(envelope.Version))
        {
            throw new InvalidOperationException("Event envelope is missing required fields.");
        }

        return envelope;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidOperationException($"{propertyName} is required.");
        }

        return element.GetString()!;
    }

    private static string ReadRequiredScalarAsString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            throw new InvalidOperationException($"{propertyName} is required.");
        }

        return element.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
            JsonValueKind.Number => element.GetRawText(),
            _ => throw new InvalidOperationException($"{propertyName} must be a string or number.")
        };
    }

    private static bool IsTransient(Exception exception)
        => exception is TimeoutException
            or HttpRequestException
            or DbUpdateException
            or OperationCanceledException;
}
