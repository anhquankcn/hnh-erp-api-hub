using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ERPApiHub.Infrastructure.Messaging;

public sealed class RabbitMqConnectionFactory(IOptions<RabbitMqOptions> options) : IRabbitMqConnectionFactory
{
    public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var rabbitMq = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = rabbitMq.Host,
            Port = rabbitMq.Port,
            UserName = rabbitMq.Username,
            Password = rabbitMq.Password,
            VirtualHost = rabbitMq.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        return factory.CreateConnectionAsync(cancellationToken);
    }
}
