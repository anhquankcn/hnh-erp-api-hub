using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Infrastructure.Messaging;

public sealed class NoOpErpNextClient(ILogger<NoOpErpNextClient> logger) : IErpNextClient
{
    public Task PushEventAsync(ErpEventEnvelope envelope, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "ERPNext client placeholder accepted event {EventId} type {EventType} from {Source}",
            envelope.EventId,
            envelope.EventType,
            envelope.Source);

        return Task.CompletedTask;
    }
}
