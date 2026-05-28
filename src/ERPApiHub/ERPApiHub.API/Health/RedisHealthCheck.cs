using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ERPApiHub.API.Health;

public sealed class RedisHealthCheck(IConnectionMultiplexer connectionMultiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var database = connectionMultiplexer.GetDatabase();
            var latency = await database.PingAsync();

            return connectionMultiplexer.IsConnected
                ? HealthCheckResult.Healthy("Redis is reachable.", new Dictionary<string, object>
                {
                    ["latency_ms"] = latency.TotalMilliseconds
                })
                : HealthCheckResult.Unhealthy("Redis multiplexer is disconnected.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable.", ex);
        }
    }
}
