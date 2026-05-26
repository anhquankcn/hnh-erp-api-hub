namespace ERPApiHub.Domain.Entities;

public sealed class WebhookSubscription : AuditableEntity
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string SystemId { get; set; } = string.Empty;
    public string[] EventTypes { get; set; } = [];
    public string WebhookUrl { get; set; } = string.Empty;
    public byte[]? SecretEncrypted { get; set; }
    public bool IsActive { get; set; } = true;

    public ExternalSystem? ExternalSystem { get; set; }
    public ICollection<WebhookDelivery> Deliveries { get; set; } = [];
}
