namespace ERPApiHub.API.DTOs.Audit;

public sealed class AuditSearchResponse
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<AuditLogDto> Items { get; set; } = [];
}
