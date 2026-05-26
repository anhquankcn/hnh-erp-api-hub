namespace ERPApiHub.Domain.Entities;

public sealed class ApiKeyMapping : AuditableEntity
{
    public string MappingId { get; set; } = string.Empty;
    public string SystemId { get; set; } = string.Empty;
    public string KeycloakUserId { get; set; } = string.Empty;
    public byte[] ErpNextApiKeyEnc { get; set; } = [];
    public byte[] ErpNextApiSecretEnc { get; set; } = [];
    public bool IsActive { get; set; } = true;

    public ExternalSystem? ExternalSystem { get; set; }
}
