using System.Text.Json;

namespace ERPApiHub.Application.Abstractions;

public interface IErpNextClient
{
    Task<ErpNextResponse<T>> GetAsync<T>(string resourcePath, CancellationToken cancellationToken);

    Task<ErpNextResponse<T>> PostAsync<T>(string resourcePath, object payload, CancellationToken cancellationToken);

    Task<ErpNextResponse<T>> PutAsync<T>(string resourcePath, object payload, CancellationToken cancellationToken);

    Task<ErpNextResponse<T>> DeleteAsync<T>(string resourcePath, CancellationToken cancellationToken);

    Task<ErpNextResponse<JsonElement[]>> GetDocTypesAsync(CancellationToken cancellationToken);
}

public sealed record ErpNextResponse<T>(
    T? Data,
    int StatusCode,
    string? Message);

public sealed record ErpEventEnvelope(
    string EventId,
    string EventType,
    string Source,
    string CorrelationId,
    DateTimeOffset Timestamp,
    string Version,
    JsonElement Payload);
