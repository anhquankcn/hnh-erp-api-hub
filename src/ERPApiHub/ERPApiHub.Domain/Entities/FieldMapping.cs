using System.ComponentModel.DataAnnotations.Schema;

namespace ERPApiHub.Domain.Entities;

public sealed class FieldMapping : AuditableEntity
{
    public FieldMapping()
    {
    }

    public FieldMapping(
        string systemId,
        string sourceField,
        string targetField,
        string dataType,
        string? transformExpression,
        bool isRequired)
    {
        SystemId = systemId;
        ExternalField = sourceField;
        ErpNextField = targetField;
        DataType = dataType;
        TransformRule = transformExpression;
        IsRequired = isRequired;
    }

    public string MappingId { get; set; } = string.Empty;
    public string SystemId { get; set; } = string.Empty;
    public string ErpNextDoctype { get; set; } = string.Empty;
    public string ExternalField { get; set; } = string.Empty;
    public string ErpNextField { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public string? TransformRule { get; set; }
    public bool IsRequired { get; set; }

    [NotMapped]
    public string SourceField
    {
        get => ExternalField;
        set => ExternalField = value;
    }

    [NotMapped]
    public string TargetField
    {
        get => ErpNextField;
        set => ErpNextField = value;
    }

    [NotMapped]
    public string? TransformExpression
    {
        get => TransformRule;
        set => TransformRule = value;
    }

    public ExternalSystem? ExternalSystem { get; set; }
}
