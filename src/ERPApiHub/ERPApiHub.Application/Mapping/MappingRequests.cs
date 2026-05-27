using System.Text.Json;

namespace ERPApiHub.Application.Mapping;

public sealed record MapRequest(
    string SourceSystem,
    string TargetSystem,
    string Doctype,
    JsonElement Payload);

public sealed record MapResponse(
    JsonElement TransformedPayload,
    List<string> AppliedMappings);
