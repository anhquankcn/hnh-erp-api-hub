using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace ERPApiHub.Infrastructure.ErpNext;

public sealed class ErpNextHttpClient : IErpNextClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _dataProtector;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ErpNextHttpClient> _logger;
    private readonly ErpNextOptions _options;

    public ErpNextHttpClient(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        IDataProtector dataProtector,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ErpNextHttpClient> logger,
        IOptions<ErpNextOptions> options)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _dataProtector = dataProtector;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ErpNextResponse<T>> GetAsync<T>(string resourcePath, CancellationToken cancellationToken)
    {
        return await GetAsync<T>(resourcePath, GetTenantId(), cancellationToken);
    }

    public async Task<ErpNextResponse<T>> GetAsync<T>(string resourcePath, string tenantId, CancellationToken cancellationToken)
    {
        return await ExecuteWithTenantAsync<T>(
            async (client, ct) =>
            {
                var response = await client.GetAsync($"/api/resource/{resourcePath}", ct);
                return await HandleResponseAsync<T>(response, ct);
            },
            tenantId,
            cancellationToken);
    }

    public async Task<ErpNextResponse<T>> PostAsync<T>(string resourcePath, object payload, CancellationToken cancellationToken)
    {
        return await ExecuteWithTenantAsync<T>(
            async (client, ct) =>
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOptions.Default),
                    Encoding.UTF8,
                    "application/json");
                var response = await client.PostAsync($"/api/resource/{resourcePath}", content, ct);
                return await HandleResponseAsync<T>(response, ct);
            },
            GetTenantId(),
            cancellationToken);
    }

    public async Task<ErpNextResponse<T>> PutAsync<T>(string resourcePath, object payload, CancellationToken cancellationToken)
    {
        return await ExecuteWithTenantAsync<T>(
            async (client, ct) =>
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOptions.Default),
                    Encoding.UTF8,
                    "application/json");
                var response = await client.PutAsync($"/api/resource/{resourcePath}", content, ct);
                return await HandleResponseAsync<T>(response, ct);
            },
            GetTenantId(),
            cancellationToken);
    }

    public async Task<ErpNextResponse<T>> DeleteAsync<T>(string resourcePath, CancellationToken cancellationToken)
    {
        return await ExecuteWithTenantAsync<T>(
            async (client, ct) =>
            {
                var response = await client.DeleteAsync($"/api/resource/{resourcePath}", ct);
                return await HandleResponseAsync<T>(response, ct);
            },
            GetTenantId(),
            cancellationToken);
    }

    public async Task<ErpNextResponse<JsonElement[]>> GetDocTypesAsync(CancellationToken cancellationToken)
    {
        return await ExecuteWithTenantAsync<JsonElement[]>(
            async (client, ct) =>
            {
                var response = await client.GetAsync("/api/resource/DocType?fields=[\"name\"]&limit_page_length=0", ct);
                var result = await HandleResponseAsync<JsonElement>(response, ct);
                if (result.Data.ValueKind == JsonValueKind.Undefined || result.Data.ValueKind != JsonValueKind.Object)
                    return new ErpNextResponse<JsonElement[]>(null, result.StatusCode, result.Message);

                if (result.Data.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                {
                    var docTypes = new List<JsonElement>();
                    foreach (var item in dataArray.EnumerateArray())
                        docTypes.Add(item);
                    return new ErpNextResponse<JsonElement[]>(docTypes.ToArray(), result.StatusCode, null);
                }

                return new ErpNextResponse<JsonElement[]>(null, result.StatusCode, result.Message);
            },
            GetTenantId(),
            cancellationToken);
    }

    private async Task<ErpNextResponse<T>> ExecuteWithTenantAsync<T>(
        Func<HttpClient, CancellationToken, Task<ErpNextResponse<T>>> action,
        string branchId,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpHubDbContext>();
        var tenant = await dbContext.TenantRegistries
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == branchId && t.DeletedAt == null, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{branchId}' not found or inactive.");

        var apiKeyMapping = await dbContext.ApiKeyMappings
            .AsNoTracking()
            .Include(m => m.ExternalSystem)
            .FirstOrDefaultAsync(m => m.ExternalSystem!.TenantId == branchId && m.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"No active API key mapping found for tenant '{branchId}'.");

        var apiKey = Encoding.UTF8.GetString(_dataProtector.Unprotect(apiKeyMapping.ErpNextApiKeyEnc));
        var apiSecret = Encoding.UTF8.GetString(_dataProtector.Unprotect(apiKeyMapping.ErpNextApiSecretEnc));
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));

        var scopedClient = new HttpClient
        {
            BaseAddress = new Uri($"https://{tenant.ErpNextHost}"),
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30)
        };
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        scopedClient.DefaultRequestHeaders.Add("X-Frappe-Site-Name", tenant.SiteName);

        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<ErpNextResponse<T>>(r => r.StatusCode >= 500 || r.StatusCode == 0)
            .WaitAndRetryAsync(
                _options.RetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (outcome, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "ERPNext request failed with {StatusCode}. Retrying in {RetryDelay}s... ({RetryCount}/{MaxRetries})",
                        outcome.Result?.StatusCode ?? 0,
                        timeSpan.TotalSeconds,
                        retryCount,
                        _options.RetryCount);
                });

        return await retryPolicy.ExecuteAsync(() => action(scopedClient, cancellationToken));
    }

    private string GetTenantId() =>
        _httpContextAccessor.HttpContext?.User.FindFirst("BranchId")?.Value
            ?? throw new UnauthorizedAccessException("BranchId claim not found in JWT.");

    private static async Task<ErpNextResponse<T>> HandleResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var data = JsonSerializer.Deserialize<T>(content, JsonOptions.Default);
                return new ErpNextResponse<T>(data, (int)response.StatusCode, null);
            }
            catch (JsonException ex)
            {
                return new ErpNextResponse<T>(default, (int)response.StatusCode, $"Deserialization failed: {ex.Message}");
            }
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.NotFound => $"Resource not found: {content}",
            HttpStatusCode.Forbidden => $"Access denied: {content}",
            HttpStatusCode.BadRequest => $"Bad request: {content}",
            _ => $"ERPNext error ({(int)response.StatusCode}): {content}"
        };

        return new ErpNextResponse<T>(default, (int)response.StatusCode, message);
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
    }
}
