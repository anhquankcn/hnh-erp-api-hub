using System.Net;
using System.Text.Json.Serialization;

namespace ERPApiHub.Application.Errors;

/// <summary>
/// RFC 7807 Problem Details response.
/// FRD ref: §12.2, §10.3
/// </summary>
public sealed class ErpHubProblemDetails
{
    /// <summary>
    /// URI reference that identifies the problem type.
    /// Pattern: https://api.hnhtravel.work/errors/{category}
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "about:blank";

    /// <summary>
    /// Short, human-readable summary of the problem type.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// HTTP status code.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    /// <summary>
    /// Detailed human-readable explanation specific to this occurrence.
    /// </summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>
    /// URI reference that identifies the specific occurrence (usually the request path).
    /// </summary>
    [JsonPropertyName("instance")]
    public string? Instance { get; init; }

    /// <summary>
    /// ULID correlation ID for this request.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>
    /// ISO 8601 UTC timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Field-level validation errors (for 400 responses).
    /// </summary>
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FieldError>? Errors { get; init; }

    /// <summary>
    /// Retry-after seconds (for 429 responses).
    /// </summary>
    [JsonPropertyName("retry_after")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryAfter { get; init; }
}

/// <summary>
/// Field-level validation error.
/// </summary>
public sealed class FieldError
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;
}