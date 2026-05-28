using ERPApiHub.Infrastructure.ErpNext;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ERPApiHub.API.Health;

public sealed class ErpNextHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<ErpNextOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = options.Value.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return HealthCheckResult.Unhealthy("ERPNext BaseUrl is not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient("ErpNextHealth");
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds));

            using var response = await client.GetAsync("/api/method/ping", cancellationToken);

            return (int)response.StatusCode < 500
                ? HealthCheckResult.Healthy("ERPNext is reachable.", new Dictionary<string, object>
                {
                    ["status_code"] = (int)response.StatusCode
                })
                : HealthCheckResult.Unhealthy($"ERPNext returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ERPNext is unreachable.", ex);
        }
    }
}
