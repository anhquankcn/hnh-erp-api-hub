namespace ERPApiHub.API.DTOs.Audit;

public sealed class AuditSearchRequest
{
    public string? TenantId { get; set; }
    public string? SystemId { get; set; }
    public string? EventType { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public string? Status { get; set; }
    public string? UserId { get; set; }
    public string? Endpoint { get; set; }
    public string? CorrelationId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; } = "createdAt";
    public string? SortDirection { get; set; } = "desc";
}
