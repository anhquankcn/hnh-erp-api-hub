namespace ERPApiHub.Application.Configuration;

/// <summary>
/// Request DTO for updating an external system registration.
/// All fields optional — only provided fields are updated.
/// FRD ref: FR-CFG-001
/// </summary>
public sealed record UpdateExternalSystemRequest
{
    public string? SystemName { get; init; }
    public string? SystemType { get; init; }
    public string? Description { get; init; }
    public string? ContactEmail { get; init; }
    public string? WebhookUrl { get; init; }
    public string? RateLimitTier { get; init; }
}