namespace ERPApiHub.Application.Webhooks;

/// <summary>
/// Configuration for ERPNext event ingestion (Server Script → API Hub).
/// FRD ref: FR-WHK-001b, §16.8
/// </summary>
public class ErpNextEventOptions
{
    public const string SectionName = "ErpNextEvents";

    /// <summary>Shared HMAC secret between ERPNext Server Scripts and API Hub.</summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>Maximum allowed clock skew in seconds for timestamp validation.</summary>
    public int MaxClockSkewSeconds { get; set; } = 300;

    /// <summary>Whether event ingestion endpoint is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether to explicitly skip signature validation for local testing.</summary>
    public bool SkipSignatureValidation { get; set; } = false;

    /// <summary>
    /// CIDR ranges or exact IP addresses allowed to call the internal ERPNext event endpoint.
    /// Empty means loopback only.
    /// </summary>
    public string[] AllowedIpRanges { get; set; } = ["127.0.0.1/32", "::1/128"];
}
