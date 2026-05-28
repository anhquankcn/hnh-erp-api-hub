namespace ERPApiHub.Application.Abstractions;

/// <summary>
/// Abstraction for blob storage (local, S3, GCS).
/// </summary>
public interface IBlobStorage
{
    Task UploadAsync(string path, Stream data, CancellationToken cancellationToken = default);
    Task<Stream?> DownloadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
}
