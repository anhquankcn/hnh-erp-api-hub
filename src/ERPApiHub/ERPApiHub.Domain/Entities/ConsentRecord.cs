namespace ERPApiHub.Domain.Entities;

public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public required string DataSubjectId { get; set; }
    public required string Purpose { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public List<string> Doctypes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
