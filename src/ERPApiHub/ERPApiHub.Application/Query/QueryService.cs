using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Cache;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Query;

public sealed class QueryService
{
    private readonly IErpNextClient _erpNextClient;
    private readonly ICacheService _cacheService;
    private readonly CacheStampedeGuard _stampedeGuard;
    private readonly CacheInvalidationService _cacheInvalidationService;
    private readonly IErpHubRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<QueryService> _logger;

    public QueryService(
        IErpNextClient erpNextClient,
        ICacheService cacheService,
        CacheStampedeGuard stampedeGuard,
        CacheInvalidationService cacheInvalidationService,
        IErpHubRepository repository,
        IHttpContextAccessor httpContextAccessor,
        ILogger<QueryService> logger)
    {
        _erpNextClient = erpNextClient;
        _cacheService = cacheService;
        _stampedeGuard = stampedeGuard;
        _cacheInvalidationService = cacheInvalidationService;
        _repository = repository;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<PaginatedResponse<JsonElement>> ListAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        return await ListAsync(request, GetTenantId(), cancellationToken);
    }

    public async Task<PaginatedResponse<JsonElement>> ListAsync(QueryRequest request, string tenantId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Check Cache-Control: no-cache
        var bypassCache = _httpContextAccessor.HttpContext?.Request.Headers.CacheControl.Contains("no-cache") == true;

        var cacheKey = $"query:{tenantId}:{request.Doctype}:{HashParams(request)}";

        PaginatedResponse<JsonElement> result;
        if (!bypassCache)
        {
            result = await _stampedeGuard.ExecuteAsync(
                cacheKey,
                async ct =>
                {
                    var cached = await _cacheService.GetAsync<PaginatedResponse<JsonElement>>(cacheKey, ct);
                    if (cached is not null)
                    {
                        _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                    }

                    return cached;
                },
                async ct =>
                {
                    var created = await FetchListAsync(request, tenantId, ct);
                    var ttl = TimeSpan.FromMinutes(5);
                    await _cacheService.SetAsync(cacheKey, created, ttl, ct);
                    await _cacheInvalidationService.RegisterQueryKeyAsync(tenantId, request.Doctype, cacheKey, ttl, ct);
                    return created;
                },
                cancellationToken);
        }
        else
        {
            result = await FetchListAsync(request, tenantId, cancellationToken);
        }

        // Audit
        await WriteAuditAsync(tenantId, GetUserId(), "GET", $"/api/v1/query/{request.Doctype}", 200, sw.ElapsedMilliseconds, cancellationToken);

        return result;
    }

    public async Task<JsonElement> GetAsync(string doctype, string name, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var tenantId = GetTenantId();
        var bypassCache = _httpContextAccessor.HttpContext?.Request.Headers.CacheControl.Contains("no-cache") == true;
        var cacheKey = $"query:{tenantId}:{doctype}:{name}";

        JsonElement result;
        if (!bypassCache)
        {
            result = await _stampedeGuard.ExecuteAsync(
                cacheKey,
                async ct => await _cacheService.GetAsync<JsonElement>(cacheKey, ct),
                async ct =>
                {
                    var created = await FetchDocumentAsync(doctype, name, ct);
                    var ttl = TimeSpan.FromMinutes(1);
                    await _cacheService.SetAsync(cacheKey, created, ttl, ct);
                    await _cacheInvalidationService.RegisterQueryKeyAsync(tenantId, doctype, cacheKey, ttl, ct);
                    return created;
                },
                cancellationToken);
        }
        else
        {
            result = await FetchDocumentAsync(doctype, name, cancellationToken);
        }

        await WriteAuditAsync(tenantId, GetUserId(), "GET", $"/api/v1/query/{doctype}/{name}", 200, sw.ElapsedMilliseconds, cancellationToken);

        return result;
    }

    public async Task<object> CountAsync(string doctype, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var tenantId = GetTenantId();
        var cacheKey = $"query:{tenantId}:{doctype}:count";
        var bypassCache = _httpContextAccessor.HttpContext?.Request.Headers.CacheControl.Contains("no-cache") == true;

        object result;
        if (!bypassCache)
        {
            result = await _stampedeGuard.ExecuteAsync(
                cacheKey,
                async ct => await _cacheService.GetAsync<object>(cacheKey, ct),
                async ct =>
                {
                    var created = await FetchCountAsync(doctype, ct);
                    var ttl = TimeSpan.FromMinutes(5);
                    await _cacheService.SetAsync(cacheKey, created, ttl, ct);
                    await _cacheInvalidationService.RegisterQueryKeyAsync(tenantId, doctype, cacheKey, ttl, ct);
                    return created;
                },
                cancellationToken);
        }
        else
        {
            result = await FetchCountAsync(doctype, cancellationToken);
        }

        await WriteAuditAsync(tenantId, GetUserId(), "GET", $"/api/v1/query/{doctype}/count", 200, sw.ElapsedMilliseconds, cancellationToken);

        return result;
    }

    public async Task PurgeCacheAsync(string doctype, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        await _cacheInvalidationService.InvalidateDoctypeAsync(doctype, tenantId, cancellationToken);
        _logger.LogInformation("Purged query cache for doctype {Doctype} tenant {Tenant}", doctype, tenantId);
    }

    private async Task<PaginatedResponse<JsonElement>> FetchListAsync(
        QueryRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var limitStart = (request.Page - 1) * request.PageSize;
        var resourcePath = BuildResourcePath(request, limitStart);

        var response = await _erpNextClient.GetAsync<JsonElement>(resourcePath, tenantId, cancellationToken);

        if (response.StatusCode != 200 || response.Data.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"ERPNext query failed: {response.Message}");
        }

        return ParseErpNextListResponse(response.Data, request);
    }

    private async Task<JsonElement> FetchDocumentAsync(
        string doctype,
        string name,
        CancellationToken cancellationToken)
    {
        var response = await _erpNextClient.GetAsync<JsonElement>($"{doctype}/{name}", cancellationToken);

        if (response.StatusCode == 404)
        {
            throw new KeyNotFoundException($"Document {doctype}/{name} not found");
        }

        if (response.StatusCode != 200 || response.Data.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"ERPNext query failed: {response.Message}");
        }

        return response.Data;
    }

    private async Task<object> FetchCountAsync(string doctype, CancellationToken cancellationToken)
    {
        var response = await _erpNextClient.GetAsync<JsonElement>($"{doctype}?limit_page_length=1&fields=[\"count(*)\"]", cancellationToken);

        if (response.StatusCode != 200 || response.Data.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"ERPNext count query failed: {response.Message}");
        }

        long count = 0;
        var data = response.Data;
        if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var first = data[0];
            if (first.TryGetProperty("count(*)", out var countProp) || first.TryGetProperty("count", out countProp))
            {
                count = countProp.GetInt64();
            }
        }

        return new { count };
    }

    private static string BuildResourcePath(QueryRequest request, int limitStart)
    {
        var path = $"{request.Doctype}?limit_start={limitStart}&limit_page_length={request.PageSize}";

        if (!string.IsNullOrWhiteSpace(request.Fields))
        {
            path += $"&fields={Uri.EscapeDataString(request.Fields)}";
        }

        if (!string.IsNullOrWhiteSpace(request.Filters))
        {
            path += $"&filters={Uri.EscapeDataString(request.Filters)}";
        }

        if (!string.IsNullOrWhiteSpace(request.OrderBy))
        {
            path += $"&order_by={Uri.EscapeDataString(request.OrderBy)}";
        }

        return path;
    }

    private static PaginatedResponse<JsonElement> ParseErpNextListResponse(JsonElement data, QueryRequest request)
    {
        var items = new List<JsonElement>();

        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    items.Add(item);
                }
            }
        }
        else if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                items.Add(item);
            }
        }

        return new PaginatedResponse<JsonElement>
        {
            Data = items,
            Pagination = new PaginationMeta
            {
                Page = request.Page,
                PageSize = request.PageSize,
                TotalCount = items.Count // ERPNext doesn't always return total; client may need separate count call
            }
        };
    }

    private static string HashParams(QueryRequest request)
    {
        var raw = $"{request.Doctype}|{request.Page}|{request.PageSize}|{request.Filters}|{request.OrderBy}|{request.Fields}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16];
    }

    private async Task WriteAuditAsync(string tenantId, string? userId, string method, string endpoint, int statusCode, long durationMs, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog
        {
            LogId = UlidGenerator.Generate(),
            RequestId = UlidGenerator.Generate(),
            TenantId = tenantId,
            UserId = userId,
            Method = method,
            Endpoint = endpoint,
            StatusCode = statusCode,
            DurationMs = (int)durationMs,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateAuditLogAsync(auditLog, cancellationToken);
    }

    private string GetTenantId() =>
        _httpContextAccessor.HttpContext?.User.FindFirst("BranchId")?.Value ?? "unknown";

    private string? GetUserId() =>
        _httpContextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}
