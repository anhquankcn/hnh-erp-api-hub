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
        _basePath = Path.GetFullPath(basePath);
        Directory.CreateDirectory(_basePath);
    }

    public async Task UploadAsync(string path, Stream data, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fileStream = File.Create(fullPath);
        await data.CopyToAsync(fileStream, cancellationToken);
    }

    public async Task<Stream?> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath)) return null;
        return File.OpenRead(fullPath);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    private string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));
        var basePath = _basePath.EndsWith(Path.DirectorySeparatorChar)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(basePath, comparison))
        {
            throw new InvalidOperationException("Blob path must remain within the configured storage root.");
        }

        return fullPath;
    }
}
