using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ERPApiHub.Infrastructure.Messaging;

public sealed class RabbitMqConnectionFactory(IOptions<RabbitMqOptions> options) : IRabbitMqConnectionFactory, IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null && _connection.IsOpen)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null && _connection.IsOpen)
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            _connection = await CreateNewConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connectionLock.Dispose();
    }

    private Task<IConnection> CreateNewConnectionAsync(CancellationToken cancellationToken)
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
