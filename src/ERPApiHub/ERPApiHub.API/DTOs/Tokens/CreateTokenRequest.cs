namespace ERPApiHub.API.DTOs.Tokens;

/// <summary>
/// Request payload for creating an API token.
/// </summary>
public sealed record CreateTokenRequest
{
    /// <summary>
    /// External system identifier the token belongs to.
    /// </summary>
    public string SystemId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable token description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Number of days before the token expires.
    /// </summary>
    public int ExpiryDays { get; init; }

    /// <summary>
    /// Permission names granted to the token.
    /// </summary>
    public string[] Permissions { get; init; } = [];
}
