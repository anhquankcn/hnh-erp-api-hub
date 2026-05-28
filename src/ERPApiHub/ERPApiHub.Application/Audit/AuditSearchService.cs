using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Audit;

public sealed class AuditSearchService
{
    private const int MaxPageSize = 500;
    private const int DefaultExportMaxRecords = 10000;
    private const int ExportBatchSize = 500;
    private readonly IErpHubRepository _repository;
    private readonly ILogger<AuditSearchService> _logger;

    public AuditSearchService(IErpHubRepository repository, ILogger<AuditSearchService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<AuditSearchResult> SearchAsync(AuditSearchQuery query, CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var (items, total) = await _repository.GetAuditLogsAsync(
            tenantId: query.TenantId,
            systemId: query.SystemId,
            eventType: query.EventType,
            fromDate: query.FromDate,
            toDate: query.ToDate,
            status: query.Status,
            userId: query.UserId,
            endpoint: query.Endpoint,
            correlationId: query.CorrelationId,
            page: page,
            pageSize: pageSize,
            sortBy: query.SortBy,
            sortDirection: query.SortDirection,
            cancellationToken: ct);

        return new AuditSearchResult(
            total,
            page,
            pageSize,
            total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize),
            items.Select(MapLog).ToList());
    }

    public async Task<Stream> ExportAsync(AuditExportQuery query, CancellationToken ct)
    {
        var format = NormalizeExportFormat(query.Format);
        var maxRecords = Math.Max(query.MaxRecords ?? DefaultExportMaxRecords, 1);
        var stream = new MemoryStream();

        if (format == "json")
        {
            await WriteJsonExportAsync(stream, query, maxRecords, ct);
        }
        else
        {
            await WriteCsvExportAsync(stream, query, maxRecords, ct);
        }

        stream.Position = 0;
        _logger.LogInformation("Exported {Format} audit log stream with max record limit {MaxRecords}.", format, maxRecords);
        return stream;
    }

    public async Task<AuditStatsResult> GetStatsAsync(DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)
    {
        var stats = new AuditStatsResult();
        var page = 1;

        while (true)
        {
            var (items, total) = await _repository.GetAuditLogsAsync(
                fromDate: fromDate,
                toDate: toDate,
                page: page,
                pageSize: ExportBatchSize,
                sortBy: "createdAt",
                sortDirection: "desc",
                cancellationToken: ct);

            foreach (var item in items)
            {
                var mapped = MapLog(item);
                stats.TotalEvents++;

                switch (mapped.Status)
                {
                    case "success":
                        stats.SuccessCount++;
                        break;
                    case "failure":
                        stats.FailureCount++;
                        break;
                    default:
                        stats.WarningCount++;
                        break;
                }

                Increment(stats.EventsByType, mapped.EventType);
                Increment(stats.EventsByTenant, mapped.TenantId);
                Increment(stats.EventsByDay, mapped.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd"));
            }

            if (items.Count == 0 || page * ExportBatchSize >= total)
            {
                break;
            }

            page++;
        }

        return stats;
    }

    private async Task WriteCsvExportAsync(Stream stream, AuditExportQuery query, int maxRecords, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("id,tenant_id,system_id,event_type,description,status,user_id,endpoint,status_code,duration_ms,correlation_id,ip_address,created_at");

        await foreach (var item in EnumerateExportLogsAsync(query, maxRecords, ct))
        {
            await writer.WriteLineAsync(string.Join(
                ',',
                EscapeCsv(item.Id),
                EscapeCsv(item.TenantId),
                EscapeCsv(item.SystemId),
                EscapeCsv(item.EventType),
                EscapeCsv(item.Description),
                EscapeCsv(item.Status),
                EscapeCsv(item.UserId),
                EscapeCsv(item.Endpoint),
                item.StatusCode?.ToString() ?? string.Empty,
                item.DurationMs?.ToString() ?? string.Empty,
                EscapeCsv(item.CorrelationId),
                EscapeCsv(item.IpAddress),
                item.CreatedAt.ToString("O")));
        }

        await writer.FlushAsync(ct);
    }

    private async Task WriteJsonExportAsync(Stream stream, AuditExportQuery query, int maxRecords, CancellationToken ct)
    {
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartArray();

        await foreach (var item in EnumerateExportLogsAsync(query, maxRecords, ct))
        {
            JsonSerializer.Serialize(writer, item);
            await writer.FlushAsync(ct);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(ct);
    }

    private async IAsyncEnumerable<AuditLogResult> EnumerateExportLogsAsync(
        AuditExportQuery query,
        int maxRecords,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var page = 1;
        var emitted = 0;

        while (emitted < maxRecords)
        {
            var pageSize = Math.Min(ExportBatchSize, maxRecords - emitted);
            var (items, _) = await _repository.GetAuditLogsAsync(
                tenantId: query.TenantId,
                systemId: query.SystemId,
                eventType: query.EventType,
                fromDate: query.FromDate,
                toDate: query.ToDate,
                status: query.Status,
                correlationId: query.CorrelationId,
                page: page,
                pageSize: pageSize,
                sortBy: "createdAt",
                sortDirection: "desc",
                cancellationToken: ct);

            if (items.Count == 0)
            {
                yield break;
            }

            foreach (var item in items)
            {
                yield return MapLog(item);
                emitted++;
            }

            page++;
        }
    }

    private static AuditLogResult MapLog(AuditLog log) =>
        new(
            log.LogId,
            log.TenantId,
            log.SystemId,
            log.Method,
            log.UserAgent,
            DeriveStatus(log.StatusCode),
            log.UserId,
            log.Endpoint,
            log.StatusCode,
            log.DurationMs,
            log.RequestId,
            log.ClientIp?.ToString(),
            log.CreatedAt);

    private static string DeriveStatus(int? statusCode)
    {
        if (statusCode is null)
        {
            return "warning";
        }

        return statusCode switch
        {
            >= 200 and < 400 => "success",
            >= 500 => "failure",
            _ => "warning"
        };
    }

    private static string NormalizeExportFormat(string? format) =>
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) ? "json" : "csv";

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static void Increment(Dictionary<string, long> values, string key)
    {
        values.TryGetValue(key, out var count);
        values[key] = count + 1;
    }
}

public sealed record AuditSearchQuery(
    string? TenantId,
    string? SystemId,
    string? EventType,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Status,
    string? UserId,
    string? Endpoint,
    string? CorrelationId,
    int Page = 1,
    int PageSize = 50,
    string SortBy = "createdAt",
    string SortDirection = "desc");

public sealed record AuditExportQuery(
    string? TenantId,
    string? SystemId,
    string? EventType,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Status,
    string Format = "csv",
    string? CorrelationId = null,
    int? MaxRecords = 10000);

public sealed record AuditSearchResult(
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<AuditLogResult> Items);

public sealed record AuditLogResult(
    string Id,
    string TenantId,
    string? SystemId,
    string EventType,
    string? Description,
    string Status,
    string? UserId,
    string? Endpoint,
    int? StatusCode,
    long? DurationMs,
    string? CorrelationId,
    string? IpAddress,
    DateTimeOffset CreatedAt);

public sealed class AuditStatsResult
{
    public long TotalEvents { get; set; }
    public long SuccessCount { get; set; }
    public long FailureCount { get; set; }
    public long WarningCount { get; set; }
    public Dictionary<string, long> EventsByType { get; set; } = [];
    public Dictionary<string, long> EventsByTenant { get; set; } = [];
    public Dictionary<string, long> EventsByDay { get; set; } = [];
}
