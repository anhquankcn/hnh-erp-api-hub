namespace ERPApiHub.API.DTOs.Webhooks;

public sealed record UpdateWebhookSubscriptionRequest
{
    public string[]? EventTypes { get; init; }
    public string? WebhookUrl { get; init; }
    public string? Secret { get; init; }
}
