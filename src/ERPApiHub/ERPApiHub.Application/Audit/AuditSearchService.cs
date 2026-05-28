using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Audit;

public sealed class AuditSearchService(
    IErpHubRepository repository,
    ILogger<AuditSearchService> logger) : IAuditSearchService
{
    private const int RepositoryPageSize = 1000;
    private const int MaxExportRows = 10000;

    public async Task<AuditSearchResult> SearchAsync(AuditSearchQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);

        var logs = await GetFilteredLogsAsync(request, null, cancellationToken);
        var totalCount = logs.Count;
        var items = logs
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();

        return new AuditSearchResult
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<Stream> ExportAsync(AuditExportQuery request, CancellationToken cancellationToken)
    {
        var format = NormalizeFormat(request.Format);
        var logs = await GetFilteredLogsAsync(request, MaxExportRows, cancellationToken);
        var items = logs.Take(MaxExportRows).Select(Map).ToList();

        logger.LogInformation("Exporting {Count} audit log rows as {Format}.", items.Count, format);

        return format switch
        {
            "json" => ToStream(JsonSerializer.Serialize(items)),
            _ => ToStream(ToCsv(items))
        };
    }

    private async Task<IReadOnlyList<AuditLog>> GetFilteredLogsAsync(
        AuditExportQuery request,
        int? maxResults,
        CancellationToken cancellationToken) =>
        await GetFilteredLogsAsync(
            new AuditSearchQuery
            {
                TenantId = request.TenantId,
                Method = request.Method,
                Endpoint = request.Endpoint,
                StatusCode = request.StatusCode,
                FromDate = request.FromDate,
                ToDate = request.ToDate,
                CorrelationId = request.CorrelationId
            },
            maxResults,
            cancellationToken);

    private async Task<IReadOnlyList<AuditLog>> GetFilteredLogsAsync(
        AuditSearchQuery request,
        int? maxResults,
        CancellationToken cancellationToken)
    {
        var results = new List<AuditLog>();
        var page = 1;
        var totalFetched = 0;
        int totalAvailable;

        do
        {
            var (items, total) = await repository.GetAuditLogsAsync(
                tenantId: Normalize(request.TenantId),
                eventType: Normalize(request.Method)?.ToUpperInvariant(),
                fromDate: request.FromDate,
                toDate: request.ToDate,
                endpoint: Normalize(request.Endpoint),
                correlationId: Normalize(request.CorrelationId),
                page: page,
                pageSize: RepositoryPageSize,
                cancellationToken: cancellationToken);

            totalAvailable = total;
            totalFetched += items.Count;

            results.AddRange(items.Where(log => MatchesStatusCodeFilter(log, request.StatusCode)));

            if (maxResults is not null && results.Count >= maxResults.Value)
            {
                break;
            }

            page++;
        }
        while (totalFetched < totalAvailable && totalFetched > 0);

        return results;
    }

    private static bool MatchesStatusCodeFilter(AuditLog log, int? statusCode) =>
        statusCode is null || log.StatusCode == statusCode;

    private static AuditLogResultItem Map(AuditLog log) => new()
    {
        Id = log.LogId,
        TenantId = log.TenantId,
        Method = log.Method,
        Endpoint = log.Endpoint,
        StatusCode = log.StatusCode ?? 0,
        DurationMs = log.DurationMs ?? 0,
        Timestamp = log.CreatedAt,
        CorrelationId = log.RequestId,
        ErrorMessage = null
    };

    private static string ToCsv(IReadOnlyList<AuditLogResultItem> logs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,TenantId,Method,Endpoint,StatusCode,DurationMs,Timestamp,CorrelationId,ErrorMessage");

        foreach (var log in logs)
        {
            sb
                .Append(Csv(log.Id)).Append(',')
                .Append(Csv(log.TenantId)).Append(',')
                .Append(Csv(log.Method)).Append(',')
                .Append(Csv(log.Endpoint)).Append(',')
                .Append(log.StatusCode).Append(',')
                .Append(log.DurationMs).Append(',')
                .Append(Csv(log.Timestamp.ToString("O"))).Append(',')
                .Append(Csv(log.CorrelationId)).Append(',')
                .Append(Csv(log.ErrorMessage))
                .AppendLine();
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static MemoryStream ToStream(string content)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        stream.Position = 0;
        return stream;
    }

    private static string NormalizeFormat(string? format) =>
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) ? "json" : "csv";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
