namespace ERPApiHub.Domain.Entities;

public sealed class FieldMapping : AuditableEntity
{
    public string MappingId { get; set; } = string.Empty;
    public string SystemId { get; set; } = string.Empty;
    public string ErpNextDoctype { get; set; } = string.Empty;
    public string ExternalField { get; set; } = string.Empty;
    public string ErpNextField { get; set; } = string.Empty;
    public string? TransformRule { get; set; }

    public ExternalSystem? ExternalSystem { get; set; }
}
