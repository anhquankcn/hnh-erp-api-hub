namespace ERPApiHub.API.DTOs.Tokens;

/// <summary>
/// API token metadata returned by token lifecycle endpoints.
/// </summary>
public sealed record TokenResponse
{
    /// <summary>
    /// Token identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// External system identifier the token belongs to.
    /// </summary>
    public string SystemId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable token description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Permission names granted to the token.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>
    /// Token lifecycle status.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Plain token value. Returned only immediately after create or rotate.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// UTC timestamp when the token was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp when the token expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// UTC timestamp when the token was last rotated.
    /// </summary>
    public DateTimeOffset? RotatedAt { get; init; }

    /// <summary>
    /// UTC timestamp when the token was revoked.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; init; }
}
