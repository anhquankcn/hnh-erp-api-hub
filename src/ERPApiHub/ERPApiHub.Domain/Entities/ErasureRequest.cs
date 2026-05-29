namespace ERPApiHub.Domain.Entities;

public sealed class ErasureRequest
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public required string DataSubjectId { get; set; }
    public string Status { get; set; } = "pending"; // pending, verified, processing, completed, failed
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? VerificationToken { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
