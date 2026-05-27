using ERPApiHub.Application.Abstractions;

namespace ERPApiHub.Infrastructure.Storage;

/// <summary>
/// Local filesystem blob storage implementation.
/// </summary>
public sealed class LocalBlobStorage : IBlobStorage
{
    private readonly string _basePath;

    public LocalBlobStorage(string basePath = "./archive")
    {
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task UploadAsync(string path, Stream data, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fileStream = File.Create(fullPath);
        await data.CopyToAsync(fileStream, cancellationToken);
    }

    public async Task<Stream?> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        if (!File.Exists(fullPath)) return null;
        return File.OpenRead(fullPath);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        return Task.FromResult(File.Exists(fullPath));
    }
}
