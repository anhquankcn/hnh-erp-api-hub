namespace ERPApiHub.API.DTOs.Tokens;

/// <summary>
/// API token validation result.
/// </summary>
public sealed record TokenValidationResponse
{
    /// <summary>
    /// Indicates whether the token is active and unexpired.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Token identifier when validation can resolve the token.
    /// </summary>
    public string? TokenId { get; init; }

    /// <summary>
    /// External system identifier associated with the token.
    /// </summary>
    public string? SystemId { get; init; }

    /// <summary>
    /// Permission names granted to the token.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>
    /// UTC timestamp when the token expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Invalid token reason, when applicable.
    /// </summary>
    public string? Reason { get; init; }
}
