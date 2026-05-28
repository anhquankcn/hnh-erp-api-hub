namespace ERPApiHub.Domain.Entities;

public sealed class TenantRegistry : AuditableEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string ErpNextHost { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = "active";
    public DateTimeOffset? LastHealthCheck { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ExternalSystem> ExternalSystems { get; set; } = [];
}
