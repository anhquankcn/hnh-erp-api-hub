using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ERPApiHub.API.HealthChecks;

public sealed class RabbitMqHealthCheck(IMessageBus messageBus) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isConnected = await messageBus.IsConnectedAsync(cancellationToken);
            return isConnected
                ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ health probe failed.", ex);
        }
    }
}
