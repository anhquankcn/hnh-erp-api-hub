using ERPApiHub.Domain.Entities;

namespace ERPApiHub.API.DTOs.Webhooks;

public sealed record WebhookSubscriptionResponse
{
    public string SubscriptionId { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public string[] EventTypes { get; init; } = [];
    public string WebhookUrl { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool HasSecret { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static WebhookSubscriptionResponse FromEntity(WebhookSubscription subscription) =>
        new()
        {
            SubscriptionId = subscription.SubscriptionId,
            SystemId = subscription.SystemId,
            EventTypes = subscription.EventTypes,
            WebhookUrl = subscription.WebhookUrl,
            IsActive = subscription.IsActive,
            HasSecret = subscription.SecretEncrypted is { Length: > 0 },
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };
}
