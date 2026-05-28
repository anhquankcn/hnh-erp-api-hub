namespace ERPApiHub.Application.Compliance;

/// <summary>
/// PDPA consent record for a data subject.
/// </summary>
public sealed record ConsentRecord
{
    public required string TenantId { get; init; }
    public required string DataSubjectId { get; init; }
    public required string Purpose { get; init; }
    public required string Status { get; init; } // "granted", "withdrawn", "pending"
    public DateTimeOffset GrantedAt { get; init; }
    public DateTimeOffset? WithdrawnAt { get; init; }
    public string? WithdrawnReason { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}
