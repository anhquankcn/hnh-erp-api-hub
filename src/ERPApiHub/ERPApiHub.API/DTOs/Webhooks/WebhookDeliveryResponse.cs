using ERPApiHub.Domain.Entities;

namespace ERPApiHub.API.DTOs.Webhooks;

public sealed record WebhookDeliveryResponse
{
    public string DeliveryId { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public string? EventType { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? HttpStatus { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ResponseBody { get; init; }

    public static WebhookDeliveryResponse FromEntity(WebhookDelivery delivery) =>
        new()
        {
            DeliveryId = delivery.DeliveryId,
            SubscriptionId = delivery.SubscriptionId,
            EventType = delivery.EventType,
            Status = delivery.Status,
            HttpStatus = delivery.HttpStatus,
            AttemptCount = delivery.AttemptCount,
            AttemptedAt = delivery.AttemptedAt,
            NextRetryAt = delivery.NextRetryAt,
            DeliveredAt = delivery.DeliveredAt,
            CreatedAt = delivery.CreatedAt,
            ResponseBody = delivery.ResponseBody
        };
}
