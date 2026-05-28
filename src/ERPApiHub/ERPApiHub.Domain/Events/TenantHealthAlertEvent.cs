namespace ERPApiHub.Domain.Events;

public sealed record TenantHealthAlertEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string Status { get; init; }
    public string? PreviousStatus { get; init; }
    public TimeSpan? ResponseTime { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset AlertedAt { get; init; }
    public string? CorrelationId { get; init; }
}
