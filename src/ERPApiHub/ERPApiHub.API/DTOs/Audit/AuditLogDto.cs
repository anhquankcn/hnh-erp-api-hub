namespace ERPApiHub.API.DTOs.Audit;

public sealed class AuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? SystemId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Endpoint { get; set; }
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
