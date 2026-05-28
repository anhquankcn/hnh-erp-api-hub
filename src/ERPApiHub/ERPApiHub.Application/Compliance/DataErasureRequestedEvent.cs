namespace ERPApiHub.Application.Compliance;

/// <summary>
/// Event published when a data erasure (right to be forgotten) is requested.
/// </summary>
public sealed record DataErasureRequestedEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string DataSubjectId { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
    public string? RequestedBy { get; init; }
    public string? CorrelationId { get; init; }
}
