using System.Text.Json;

namespace ERPApiHub.Application.Abstractions;

public interface IErpNextClient
{
    Task PushEventAsync(ErpEventEnvelope envelope, CancellationToken cancellationToken);
}

public sealed record ErpEventEnvelope(
    string EventId,
    string EventType,
    string Source,
    string CorrelationId,
    DateTimeOffset Timestamp,
    string Version,
    JsonElement Payload);
