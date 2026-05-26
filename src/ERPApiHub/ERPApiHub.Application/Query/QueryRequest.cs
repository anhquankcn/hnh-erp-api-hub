namespace ERPApiHub.Application.Query;

public sealed record QueryRequest
{
    public string Doctype { get; init; } = string.Empty;
    public string? Name { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Filters { get; init; }
    public string? OrderBy { get; init; }
    public string? Fields { get; init; }
}