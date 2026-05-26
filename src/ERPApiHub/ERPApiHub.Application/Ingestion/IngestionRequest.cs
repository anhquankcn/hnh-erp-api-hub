using System.Text.Json;

namespace ERPApiHub.Application.Ingestion;

public sealed record IngestionRequest(
    string Doctype,
    JsonElement Payload,
    string? Name = null,
    string? IdempotencyKey = null);
