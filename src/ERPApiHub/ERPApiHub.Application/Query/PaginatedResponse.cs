namespace ERPApiHub.Application.Query;

public sealed record PaginatedResponse<T>
{
    public List<T> Data { get; init; } = [];
    public PaginationMeta Pagination { get; init; } = new();
}

public sealed record PaginationMeta
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}