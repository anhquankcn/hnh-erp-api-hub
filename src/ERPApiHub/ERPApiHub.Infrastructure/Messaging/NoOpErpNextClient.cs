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

    public Task<ErpNextResponse<T>> PostAsync<T>(string resourcePath, object payload, CancellationToken cancellationToken)
    {
        logger.LogInformation("ERPNext client placeholder accepted POST {ResourcePath}", resourcePath);

        return Task.FromResult(new ErpNextResponse<T>(default, 202, "No-op ERPNext POST accepted."));
    }
}
