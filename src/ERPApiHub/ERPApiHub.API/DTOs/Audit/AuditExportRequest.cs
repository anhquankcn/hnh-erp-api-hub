namespace ERPApiHub.API.DTOs.Audit;

public sealed record AuditExportRequest
{
    public string? TenantId { get; init; }
    public string? Method { get; init; }
    public string? Endpoint { get; init; }
    public int? StatusCode { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public string? CorrelationId { get; init; }
    public string Format { get; init; } = "csv";
}
