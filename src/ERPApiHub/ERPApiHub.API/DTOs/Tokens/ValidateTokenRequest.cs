namespace ERPApiHub.API.DTOs.Tokens;

/// <summary>
/// Request payload for validating an API token.
/// </summary>
public sealed record ValidateTokenRequest
{
    /// <summary>
    /// Plain API token to validate.
    /// </summary>
    public string Token { get; init; } = string.Empty;
}
