namespace ERPApiHub.Domain.Entities;

public sealed class WebhookDelivery
{
    public string DeliveryId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? HttpStatus { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }

    public WebhookSubscription? Subscription { get; set; }
}
