using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Infrastructure.Messaging;

public sealed class NoOpErpNextClient(ILogger<NoOpErpNextClient> logger) : IErpNextClient
{
    public Task<ErpNextResponse<T>> GetAsync<T>(string resourcePath, CancellationToken cancellationToken)
    {
        logger.LogInformation("ERPNext client placeholder accepted GET {ResourcePath}", resourcePath);
        return Task.FromResult(new ErpNextResponse<T>(default, 200, "No-op ERPNext GET accepted."));
    }

    public Task<ErpNextResponse<T>> GetAsync<T>(string resourcePath, string tenantId, CancellationToken cancellationToken)
    {
        logger.LogInformation("ERPNext client placeholder accepted GET {ResourcePath} for tenant {TenantId}", resourcePath, tenantId);
        return Task.FromResult(new ErpNextResponse<T>(default, 200, "No-op ERPNext GET accepted."));
    }

    public Task<ErpNextResponse<T>> PostAsync<T>(string resourcePath, object payload, CancellationToken cancellationToken)
    {
        logger.LogInformation("ERPNext client placeholder accepted POST {ResourcePath}", resourcePath);
        return Task.FromResult(new ErpNextResponse<T>(default, 202, "No-op ERPNext POST accepted."));
    }

    public Task<ErpNextResponse<T>> PutAsync<T>(string resourcePath, object payload, CancellationToken cancellationToken)
    {
        logger.LogInformation("ERPNext client placeholder accepted PUT {ResourcePath}", resourcePath);
        return Task.FromResult(new ErpNextResponse<T>(default, 200, "No-op ERPNext PUT accepted."));
    }

    public Task<ErpNextResponse<T>> DeleteAsync<T>(string resourcePath, CancellationToken cancellationToken)
    {
        logger.LogInformation("ERPNext client placeholder accepted DELETE {ResourcePath}", resourcePath);
        return Task.FromResult(new ErpNextResponse<T>(default, 200, "No-op ERPNext DELETE accepted."));
    }

    public Task<ErpNextResponse<JsonElement[]>> GetDocTypesAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("ERPNext client placeholder returning empty DocTypes list");
        return Task.FromResult(new ErpNextResponse<JsonElement[]>([], 200, "No-op ERPNext GetDocTypes accepted."));
    }
}
