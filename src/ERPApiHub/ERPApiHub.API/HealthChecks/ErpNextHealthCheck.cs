using ERPApiHub.Infrastructure.ErpNext;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ERPApiHub.API.HealthChecks;

public sealed class ErpNextHealthCheck(HttpClient httpClient, IOptions<ErpNextOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                return HealthCheckResult.Unhealthy("ERPNext BaseUrl is invalid.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(baseUri, "/api/method/ping"));
            using var response = await httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("ERPNext ping succeeded.")
                : HealthCheckResult.Unhealthy($"ERPNext ping returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ERPNext health probe failed.", ex);
        }
    }
}
