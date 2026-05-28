using System.Net;

namespace ERPApiHub.Domain.Entities;

public sealed class AuditLog
{
    public string LogId { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string? SystemId { get; set; }
    public string? UserId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public int? DurationMs { get; set; }
    public int? RequestSizeBytes { get; set; }
    public int? ResponseSizeBytes { get; set; }
    public IPAddress? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ExternalSystem? ExternalSystem { get; set; }
}
