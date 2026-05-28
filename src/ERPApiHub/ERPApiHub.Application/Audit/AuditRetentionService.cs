using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.Audit;

/// <summary>
/// Background service for audit log retention and archival.
/// Runs daily to archive old logs and maintain hash chain integrity.
/// </summary>
public sealed class AuditRetentionService : BackgroundService
{
    private const string LastHashCacheKey = "audit:last-hash";
    private const string LastRunCacheKey = "audit:last-run";

    private readonly IErpHubRepository _repository;
    private readonly IBlobStorage _blobStorage;
    private readonly ICacheService _cache;
    private readonly IOptions<RetentionConfig> _options;
    private readonly ILogger<AuditRetentionService> _logger;
    private string? _lastHash;

    public AuditRetentionService(
        IErpHubRepository repository,
        IBlobStorage blobStorage,
        ICacheService cache,
        IOptions<RetentionConfig> options,
        ILogger<AuditRetentionService> logger)
    {
        _repository = repository;
        _blobStorage = blobStorage;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditRetentionService started. Retention: {RetentionDays} days, Mode: {Mode}",
            _options.Value.DefaultRetentionDays, _options.Value.ArchiveMode);

        await RestoreLastHashAsync(stoppingToken);

        // Run retention check on startup
        await RunRetentionAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Value.RunInterval, stoppingToken);
                await RunRetentionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audit retention run");
            }
        }

        _logger.LogInformation("AuditRetentionService stopped");
    }

    public async Task RunRetentionAsync(CancellationToken cancellationToken = default)
    {
        if (_lastHash is null)
        {
            await RestoreLastHashAsync(cancellationToken);
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.Value.DefaultRetentionDays);
        _logger.LogInformation("Running audit retention for logs older than {Cutoff:O}", cutoff);

        var oldLogs = await _repository.GetAuditLogsOlderThanAsync(cutoff, _options.Value.BatchSize, cancellationToken);
        if (oldLogs.Count == 0)
        {
            _logger.LogInformation("No audit logs to archive");
            return;
        }

        _logger.LogInformation("Archiving {Count} audit logs", oldLogs.Count);

        var archived = new List<ArchivedAuditLog>();
        foreach (var log in oldLogs)
        {
            var hash = ComputeHash(log, _lastHash);
            var archivedLog = new ArchivedAuditLog
            {
                Id = log.LogId,
                EventType = log.Method,
                EntityType = "http_request",
                EntityId = log.RequestId ?? log.LogId,
                Action = log.Endpoint,
                PerformedBy = log.UserId ?? "anonymous",
                PerformedAt = log.CreatedAt,
                Details = JsonSerializer.Serialize(new
                {
                    log.StatusCode,
                    log.DurationMs,
                    log.RequestSizeBytes,
                    log.ResponseSizeBytes,
                    ClientIp = log.ClientIp?.ToString(),
                    log.UserAgent
                }),
                TenantId = log.TenantId,
                ArchiveDate = DateTimeOffset.UtcNow,
                Hash = hash,
                PreviousHash = _lastHash,
            };
            archived.Add(archivedLog);
            _lastHash = hash;
        }

        await _cache.SetAsync(LastHashCacheKey, _lastHash, cancellationToken: cancellationToken);

        // Write to blob storage
        var archivePath = $"audit/{DateTimeOffset.UtcNow:yyyy/MM/dd}/archive-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
        var json = JsonSerializer.Serialize(archived, new JsonSerializerOptions { WriteIndented = true });
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await _blobStorage.UploadAsync(archivePath, stream, cancellationToken);

        // Delete archived logs from DB
        await _repository.DeleteAuditLogsAsync(oldLogs.Select(l => l.LogId).ToList(), cancellationToken);

        await _cache.SetAsync(LastRunCacheKey, DateTimeOffset.UtcNow, cancellationToken: cancellationToken);

        _logger.LogInformation("Archived {Count} logs to {Path}", archived.Count, archivePath);
    }

    public async Task<RetentionStatus> GetRetentionStatusAsync(CancellationToken cancellationToken = default)
    {
        var totalLogs = await _repository.CountAuditLogsAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.Value.DefaultRetentionDays);
        var oldLogsCount = await _repository.CountAuditLogsOlderThanAsync(cutoff, cancellationToken);
        var lastRun = await _cache.GetAsync<DateTimeOffset?>(LastRunCacheKey, cancellationToken);

        return new RetentionStatus
        {
            TotalLogs = totalLogs,
            LogsPendingArchive = oldLogsCount,
            RetentionDays = _options.Value.DefaultRetentionDays,
            ArchiveMode = _options.Value.ArchiveMode,
            LastRun = lastRun,
            HashChainEnabled = _options.Value.EnableHashChain,
        };
    }

    private async Task RestoreLastHashAsync(CancellationToken cancellationToken)
    {
        _lastHash = await _cache.GetAsync<string>(LastHashCacheKey, cancellationToken);
    }

    private string ComputeHash(AuditLog log, string? previousHash)
    {
        if (!_options.Value.EnableHashChain)
            return string.Empty;

        var input = $"{log.LogId}:{log.Method}:{log.Endpoint}:{log.RequestId}:{log.StatusCode}:{log.CreatedAt:O}:{previousHash ?? "genesis"}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

public sealed class RetentionStatus
{
    public required int TotalLogs { get; init; }
    public required int LogsPendingArchive { get; init; }
    public required int RetentionDays { get; init; }
    public required string ArchiveMode { get; init; }
    public required DateTimeOffset? LastRun { get; init; }
    public required bool HashChainEnabled { get; init; }
}
