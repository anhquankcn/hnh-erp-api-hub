namespace ERPApiHub.Application.Compliance;

/// <summary>
/// Event published when a data subject withdraws consent.
/// </summary>
public sealed record ConsentWithdrawnEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string DataSubjectId { get; init; }
    public required string Purpose { get; init; }
    public DateTimeOffset WithdrawnAt { get; init; }
    public string? Reason { get; init; }
    public string? CorrelationId { get; init; }
}
