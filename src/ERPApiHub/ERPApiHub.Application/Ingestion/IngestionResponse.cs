namespace ERPApiHub.Application.Ingestion;

public sealed record IngestionResponse(
    string JobId,
    string Status,
    string CorrelationId);
