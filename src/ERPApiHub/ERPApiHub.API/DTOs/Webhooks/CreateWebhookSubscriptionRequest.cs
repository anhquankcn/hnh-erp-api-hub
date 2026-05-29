namespace ERPApiHub.API.DTOs.Webhooks;

public sealed record CreateWebhookSubscriptionRequest
{
    public string SystemId { get; init; } = string.Empty;
    public string[] EventTypes { get; init; } = [];
    public string WebhookUrl { get; init; } = string.Empty;
    public string? Secret { get; init; }
}
