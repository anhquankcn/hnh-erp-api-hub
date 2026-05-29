namespace ERPApiHub.API.DTOs.Audit;

public sealed class AuditExportRequest
{
    public string? TenantId { get; set; }
    public string? SystemId { get; set; }
    public string? EventType { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public string? Status { get; set; }
    public string Format { get; set; } = "csv";
    public string? CorrelationId { get; set; }
    public int? MaxRecords { get; set; } = 10000;
}
