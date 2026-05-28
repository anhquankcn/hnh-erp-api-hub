using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Query;

public sealed class QueryService
{
    private readonly IErpNextClient _erpNextClient;
    private readonly ICacheService _cacheService;
    private readonly IErpHubRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<QueryService> _logger;

    public QueryService(
        IErpNextClient erpNextClient,
        ICacheService cacheService,
        IErpHubRepository repository,
        IHttpContextAccessor httpContextAccessor,
        ILogger<QueryService> logger)
    {
        _erpNextClient = erpNextClient;
        _cacheService = cacheService;
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

        if (!bypassCache)
        {
            PaginatedResponse<JsonElement>? cached = await _cacheService.GetAsync<PaginatedResponse<JsonElement>>(cacheKey, cancellationToken);
            if (cached is not null)
            {
                _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                return cached;
            }
        }

        // Build ERPNext resource path with query params
        var limitStart = (request.Page - 1) * request.PageSize;
        var resourcePath = BuildResourcePath(request, limitStart);

        var response = await _erpNextClient.GetAsync<JsonElement>(resourcePath, tenantId, cancellationToken);

        if (response.StatusCode != 200 || response.Data.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"ERPNext query failed: {response.Message}");
        }

        // Parse ERPNext response
        var result = ParseErpNextListResponse(response.Data, request);

        // Cache with TTL 5 min for lists
        if (!bypassCache)
        {
            await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5), cancellationToken);
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

        if (!bypassCache)
        {
            JsonElement? cached = await _cacheService.GetAsync<JsonElement>(cacheKey, cancellationToken);
            if (cached.HasValue)
            {
                return cached.GetValueOrDefault();
            }
        }

        var response = await _erpNextClient.GetAsync<JsonElement>($"{doctype}/{name}", cancellationToken);

        if (response.StatusCode == 404)
        {
            throw new KeyNotFoundException($"Document {doctype}/{name} not found");
        }

        if (response.StatusCode != 200 || response.Data.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"ERPNext query failed: {response.Message}");
        }

        // Cache with TTL 1 min for single docs
        if (!bypassCache)
        {
            await _cacheService.SetAsync(cacheKey, response.Data, TimeSpan.FromMinutes(1), cancellationToken);
        }

        await WriteAuditAsync(tenantId, GetUserId(), "GET", $"/api/v1/query/{doctype}/{name}", 200, sw.ElapsedMilliseconds, cancellationToken);

        return response.Data;
    }

    public async Task<object> CountAsync(string doctype, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var tenantId = GetTenantId();
        var cacheKey = $"query:{tenantId}:{doctype}:count";
        var bypassCache = _httpContextAccessor.HttpContext?.Request.Headers.CacheControl.Contains("no-cache") == true;

        if (!bypassCache)
        {
            var cached = await _cacheService.GetAsync<object>(cacheKey, cancellationToken);
            if (cached is not null) return cached;
        }

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

        var result = new { count };

        if (!bypassCache)
        {
            await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5), cancellationToken);
        }

        await WriteAuditAsync(tenantId, GetUserId(), "GET", $"/api/v1/query/{doctype}/count", 200, sw.ElapsedMilliseconds, cancellationToken);

        return result;
    }

    public async Task PurgeCacheAsync(string doctype, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        // Purge by pattern — note: requires SCAN in production; simplified here
        var patterns = new[] { $"query:{tenantId}:{doctype}:*", $"query:{tenantId}:{doctype}:count" };
        foreach (var pattern in patterns)
        {
            await _cacheService.RemoveAsync(pattern, cancellationToken);
        }
        _logger.LogInformation("Purged query cache for doctype {Doctype} tenant {Tenant}", doctype, tenantId);
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
