using ERPApiHub.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ERPApiHub.Worker.Health;

public sealed class RabbitMqHealthCheck(IRabbitMqConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", ex);
        }
    }
}
