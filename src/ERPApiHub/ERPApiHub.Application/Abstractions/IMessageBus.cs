namespace ERPApiHub.Application.Abstractions;

public interface IMessageBus
{
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken = default);
    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);
}
