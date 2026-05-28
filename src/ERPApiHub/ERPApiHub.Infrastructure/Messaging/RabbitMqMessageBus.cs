using System.Reflection;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ERPApiHub.Infrastructure.Messaging;

public sealed class RabbitMqMessageBus(
    IRabbitMqConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options) : IMessageBus
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync<T>(
        string exchange,
        string routingKey,
        T message,
        CancellationToken cancellationToken = default)
    {
        var rabbitMqOptions = options.Value;
        var targetExchange = string.IsNullOrWhiteSpace(exchange) ? rabbitMqOptions.ExchangeName : exchange;

        await using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, SerializerOptions));
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = GetStringProperty(message, "EventId"),
            CorrelationId = GetStringProperty(message, "CorrelationId")
        };

        await channel.BasicPublishAsync(
            targetExchange,
            routingKey,
            false,
            properties,
            body,
            cancellationToken);
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
        return connection.IsOpen;
    }

    private static string? GetStringProperty<T>(T message, string propertyName)
    {
        if (message is null)
        {
            return null;
        }

        return typeof(T).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(message) as string;
    }
}
