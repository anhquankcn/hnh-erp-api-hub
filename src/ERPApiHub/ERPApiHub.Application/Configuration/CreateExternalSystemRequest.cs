namespace ERPApiHub.Application.Configuration;

/// <summary>
/// Request DTO for creating an external system registration.
/// FRD ref: FR-CFG-001
/// </summary>
public sealed record CreateExternalSystemRequest
{
    /// <summary>Unique system name (slug format, unique per tenant).</summary>
    public string SystemName { get; init; } = string.Empty;

    /// <summary>System type (CRM, Ticketing, Payment, Website, Partner, etc.).</summary>
    public string SystemType { get; init; } = string.Empty;

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>Contact email for the external system admin.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Default webhook callback URL (HTTPS required).</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Rate limit tier: TIER_1, TIER_2, TIER_3. Default: TIER_2.</summary>
    public string RateLimitTier { get; init; } = "TIER_2";

    /// <summary>ERPNext API key for this system (provided by admin).</summary>
    public string ErpNextApiKey { get; init; } = string.Empty;

    /// <summary>ERPNext API secret for this system (provided by admin).</summary>
    public string ErpNextApiSecret { get; init; } = string.Empty;
}