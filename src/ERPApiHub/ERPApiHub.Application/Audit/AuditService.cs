using System.Diagnostics;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Audit;

public sealed class AuditService
{
    private readonly IErpHubRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PiiMaskingService _piiMasking;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IErpHubRepository repository,
        IHttpContextAccessor httpContextAccessor,
        PiiMaskingService piiMasking,
        ILogger<AuditService> logger)
    {
        _repository = repository;
        _httpContextAccessor = httpContextAccessor;
        _piiMasking = piiMasking;
        _logger = logger;
    }

    public async Task<object> QueryLogsAsync(
        string? tenantId = null,
        string? userId = null,
        string? endpoint = null,
        int? statusCode = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var (logs, totalCount) = await _repository.GetAuditLogsAsync(
            tenantId, null, fromDate, toDate, page, pageSize, cancellationToken);

        // Apply PII masking
        var maskedLogs = logs.Select(l => new
        {
            l.LogId,
            l.RequestId,
            l.TenantId,
            UserId = l.UserId != null ? _piiMasking.MaskText(l.UserId, maskEmails: true, maskPhones: false) : null,
            l.Method,
            l.Endpoint,
            l.StatusCode,
            l.DurationMs,
            UserAgent = l.UserAgent != null ? _piiMasking.MaskText(l.UserAgent, maskEmails: true, maskPhones: true) : null,
            l.CreatedAt
        }).ToList();

        return new
        {
            Data = maskedLogs,
            Pagination = new
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            }
        };
    }

    public async Task<string> ExportLogsAsCsvAsync(
        string? tenantId = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var (logs, _) = await _repository.GetAuditLogsAsync(
            tenantId, null, fromDate, toDate, 1, 10000, cancellationToken);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("log_id,tenant_id,user_id,method,endpoint,status_code,duration_ms,created_at");

        foreach (var l in logs)
        {
            var userId = l.UserId != null ? _piiMasking.MaskText(l.UserId, maskEmails: true, maskPhones: false) : "";
            sb.AppendLine($"{l.LogId},{l.TenantId},{userId},{l.Method},{l.Endpoint},{l.StatusCode},{l.DurationMs},{l.CreatedAt:O}");
        }

        return sb.ToString();
    }
}
