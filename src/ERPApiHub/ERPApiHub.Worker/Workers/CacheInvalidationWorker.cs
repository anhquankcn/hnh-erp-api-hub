using System.Text.Json;
using ERPApiHub.Application.Cache;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ERPApiHub.Worker.Workers;

public sealed class CacheInvalidationWorker : BackgroundService
{
    private const string QueueName = "erphub.cache.invalidation";
    private const string DeadLetterQueueName = "erphub.cache.invalidation.dlq";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ILogger<CacheInvalidationWorker> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public CacheInvalidationWorker(
        IServiceScopeFactory scopeFactory,
        IRabbitMqConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<CacheInvalidationWorker> logger)
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
            _rabbitMqOptions.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = DeadLetterQueueName
        };

        await _channel.QueueDeclareAsync(
            QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            QueueName,
            _rabbitMqOptions.ExchangeName,
            "erphub.webhook.#",
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(0, 8, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, args) => await ProcessAsync(args, stoppingToken);

        await _channel.BasicConsumeAsync(
            QueueName,
            autoAck: false,
            consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Cache invalidation worker consuming from {Queue} bound to {Exchange}.",
            QueueName,
            _rabbitMqOptions.ExchangeName);

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

    private async Task ProcessAsync(BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(args.Body.Span);
            if (!TryExtractDoctype(document.RootElement, out var doctype))
            {
                await _channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
                return;
            }

            var tenantId = TryExtractString(document.RootElement, "tenantId")
                ?? TryExtractString(document.RootElement, "branchId")
                ?? TryExtractString(document.RootElement, "BranchId");

            await using var scope = _scopeFactory.CreateAsyncScope();
            var invalidationService = scope.ServiceProvider.GetRequiredService<CacheInvalidationService>();
            await invalidationService.InvalidateDoctypeAsync(doctype, tenantId, cancellationToken);

            await _channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid cache invalidation event. Dead-lettering message.");
            await _channel.BasicNackAsync(args.DeliveryTag, false, false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache from webhook event. Dead-lettering message.");
            await _channel.BasicNackAsync(args.DeliveryTag, false, false, cancellationToken);
        }
    }

    private static bool TryExtractDoctype(JsonElement root, out string doctype)
    {
        doctype = TryExtractString(root, "doctype")
            ?? (root.TryGetProperty("payload", out var payload) ? TryExtractString(payload, "doctype") : null)
            ?? string.Empty;

        return !string.IsNullOrWhiteSpace(doctype);
    }

    private static string? TryExtractString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;
}
