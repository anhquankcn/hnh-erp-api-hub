namespace ERPApiHub.API.DTOs.Tokens;

/// <summary>
/// Paginated API token list response.
/// </summary>
public sealed record TokenListResponse
{
    /// <summary>
    /// Token records on the current page.
    /// </summary>
    public IReadOnlyList<TokenResponse> Items { get; init; } = [];

    /// <summary>
    /// Total number of matching token records.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Number of records requested per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; init; }
}
