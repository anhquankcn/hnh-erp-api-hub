namespace ERPApiHub.API.DTOs.Audit;

public sealed record AuditSearchRequest
{
    public string? TenantId { get; init; }
    public string? Method { get; init; }
    public string? Endpoint { get; init; }
    public int? StatusCode { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public string? CorrelationId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
