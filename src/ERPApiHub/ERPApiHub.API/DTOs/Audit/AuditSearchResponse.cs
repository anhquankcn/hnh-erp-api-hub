namespace ERPApiHub.API.DTOs.Audit;

public sealed record AuditSearchResponse
{
    public IReadOnlyList<AuditLogItem> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}
