namespace ERPApiHub.Application.Webhooks;

public sealed class WebhookDispatcherOptions
{
    public const string SectionName = "Webhooks:Dispatcher";

    public int MaxAttempts { get; set; } = 3;

    public int InitialBackoffSeconds { get; set; } = 1;

    public int DedupTtlHours { get; set; } = 24;
}
