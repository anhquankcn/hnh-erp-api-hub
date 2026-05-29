namespace ERPApiHub.Application.Webhooks;

public sealed record WebhookDispatchResult(
    string EventId,
    string EventType,
    int MatchedSubscriptions,
    int Delivered,
    int Failed,
    int SkippedDuplicates)
{
    public bool AllDelivered => Failed == 0;
}
