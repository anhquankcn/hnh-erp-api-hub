namespace ERPApiHub.Application.Compliance;

/// <summary>
/// Async data portability export job.
/// </summary>
public sealed record ExportJob
{
    public required string JobId { get; init; }
    public required string TenantId { get; init; }
    public required string Status { get; init; } // "queued", "processing", "completed", "failed"
    public string? DownloadUrl { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ErrorMessage { get; init; }
}
