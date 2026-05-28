namespace ERPApiHub.Application.Audit;

/// <summary>
/// Configuration for audit log retention and archival.
/// </summary>
public sealed class RetentionConfig
{
    public int DefaultRetentionDays { get; set; } = 90;
    public string ArchiveMode { get; set; } = "local"; // "local", "s3", "gcs"
    public string ArchivePath { get; set; } = "./archive";
    public string HashAlgorithm { get; set; } = "SHA256";
    public bool EnableHashChain { get; set; } = true;
    public int BatchSize { get; set; } = 1000;
    public TimeSpan RunInterval { get; set; } = TimeSpan.FromHours(24);
}
