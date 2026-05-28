using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ERPApiHub.API.HealthChecks;

public sealed class RedisHealthCheck(ICacheService cacheService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await cacheService.PingAsync(cancellationToken);
            return isHealthy
                ? HealthCheckResult.Healthy("Redis ping succeeded.")
                : HealthCheckResult.Unhealthy("Redis ping failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis health probe failed.", ex);
        }
    }
}
