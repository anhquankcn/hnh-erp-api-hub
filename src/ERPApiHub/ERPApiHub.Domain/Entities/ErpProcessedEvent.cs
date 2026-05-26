namespace ERPApiHub.Domain.Entities;

public sealed class ErpProcessedEvent
{
    public string ErpProcessedEventId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}
