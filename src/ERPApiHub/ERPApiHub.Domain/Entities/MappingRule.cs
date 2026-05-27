namespace ERPApiHub.Domain.Entities;

public sealed class MappingRule
{
    public string Id { get; set; } = string.Empty;
    public string SourceField { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public string Doctype { get; set; } = string.Empty;
    public string SourceSystemId { get; set; } = string.Empty;
    public string TargetSystemId { get; set; } = string.Empty;
    public string? Format { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
