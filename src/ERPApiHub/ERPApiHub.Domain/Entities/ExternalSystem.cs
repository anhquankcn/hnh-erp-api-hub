namespace ERPApiHub.Domain.Entities;

public sealed class ExternalSystem : AuditableEntity
{
    public string SystemId { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string SystemType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public string RateLimitTier { get; set; } = "TIER_2";
    public string TenantId { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public TenantRegistry? Tenant { get; set; }
    public ICollection<ApiKeyMapping> ApiKeyMappings { get; set; } = [];
    public ICollection<FieldMapping> FieldMappings { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<WebhookSubscription> WebhookSubscriptions { get; set; } = [];
}
