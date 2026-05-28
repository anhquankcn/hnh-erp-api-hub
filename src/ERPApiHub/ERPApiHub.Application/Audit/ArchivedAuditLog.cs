using ERPApiHub.Domain.Entities;

namespace ERPApiHub.Application.Audit;

/// <summary>
/// Archived audit log with tamper-proof hash chain.
/// </summary>
public sealed class ArchivedAuditLog
{
    public required string Id { get; init; }
    public required string EventType { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string Action { get; init; }
    public required string PerformedBy { get; init; }
    public required DateTimeOffset PerformedAt { get; init; }
    public string? Details { get; init; }
    public string? TenantId { get; init; }
    public required DateTimeOffset ArchiveDate { get; init; }
    public required string Hash { get; init; }
    public string? PreviousHash { get; init; }
}
