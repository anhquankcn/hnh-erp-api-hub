using System.Diagnostics;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetUlid;

namespace ERPApiHub.Application.Audit;

public sealed class AuditService
{
    private readonly ErpHubDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PiiMaskingService _piiMasking;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        ErpHubDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        PiiMaskingService piiMasking,
        ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
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
        var query = _dbContext.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(a => a.UserId == userId);

        if (!string.IsNullOrWhiteSpace(endpoint))
            query = query.Where(a => a.Endpoint.Contains(endpoint));

        if (statusCode.HasValue)
            query = query.Where(a => a.StatusCode == statusCode);

        if (fromDate.HasValue)
            query = query.Where(a => a.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(a => a.CreatedAt <= toDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.LogId,
                a.RequestId,
                a.TenantId,
                a.UserId,
                a.Method,
                a.Endpoint,
                a.StatusCode,
                a.DurationMs,
                UserAgent = a.UserAgent,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

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
        var query = _dbContext.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        if (fromDate.HasValue)
            query = query.Where(a => a.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(a => a.CreatedAt <= toDate.Value);

        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(10000) // Cap export
            .Select(a => new { a.LogId, a.TenantId, a.UserId, a.Method, a.Endpoint, a.StatusCode, a.DurationMs, a.CreatedAt })
            .ToListAsync(cancellationToken);

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