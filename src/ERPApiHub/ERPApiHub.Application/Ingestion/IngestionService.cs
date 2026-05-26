using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Caching;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ERPApiHub.Application.Ingestion;

public interface IIngestionService
{
    Task<IngestionResponse> IngestAsync(
        string doctype,
        JsonElement payload,
        string? name,
        CancellationToken cancellationToken);

    Task<IngestionResponse> UpdateAsync(
        string doctype,
        string name,
        JsonElement payload,
        CancellationToken cancellationToken);

    Task<IngestionResponse> DeleteAsync(
        string doctype,
        string name,
        CancellationToken cancellationToken);

    Task<IngestionResponse> BatchIngestAsync(
        IReadOnlyList<BatchOperation> operations,
        CancellationToken cancellationToken);

    Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken);
}

public sealed record BatchOperation(
    string Doctype,
    JsonElement Payload,
    string? Name = null);

public sealed record JobStatusResponse(
    string JobId,
    string Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

public sealed class IngestionService : IIngestionService
{
    private readonly IAllowedDoctypeValidator _doctypeValidator;
    private readonly IRedisCacheService _cache;
    private readonly ErpHubDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRabbitMqConnectionFactory _rabbitMqFactory;
    private readonly IOptions<RabbitMqOptions> _rabbitMqOptions;
    private readonly ILogger<IngestionService> _logger;
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(24);

    public IngestionService(
        IAllowedDoctypeValidator doctypeValidator,
        IRedisCacheService cache,
        ErpHubDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        IRabbitMqConnectionFactory rabbitMqFactory,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<IngestionService> logger)
    {
        _doctypeValidator = doctypeValidator;
        _cache = cache;
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _rabbitMqFactory = rabbitMqFactory;
        _rabbitMqOptions = rabbitMqOptions;
        _logger = logger;
    }

    public async Task<IngestionResponse> IngestAsync(
        string doctype,
        JsonElement payload,
        string? name,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var correlationId = GetCorrelationId();
        var idempotencyKey = GetIdempotencyKey();
        var jobId = UlidGenerator.Generate();

        // Idempotency check
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var idempotencyCacheKey = $"idempotency:{tenantId}:{idempotencyKey}";
            var cached = await _cache.GetAsync<IngestionResponse>(idempotencyCacheKey, cancellationToken);
            if (cached is not null)
            {
                _logger.LogInformation("Idempotency cache hit for key {IdempotencyKey}", idempotencyKey);
                return cached;
            }
        }

        // Validate doctype
        if (!_doctypeValidator.IsAllowed(doctype))
        {
            throw new ArgumentException($"Doctype '{doctype}' is not in the allowed list.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Publish to RabbitMQ
            await PublishEventAsync(
                doctype,
                "created",
                payload,
                name,
                jobId,
                correlationId,
                tenantId,
                cancellationToken);

            // Store job status in Redis
            var jobStatus = new JobStatusResponse(jobId, "pending", DateTimeOffset.UtcNow, null, null);
            await _cache.SetAsync($"job:{jobId}", jobStatus, JobTtl, cancellationToken);

            // Audit log
            await AuditAsync(tenantId, "POST", $"/api/v1/ingest/{doctype}", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            var response = new IngestionResponse(jobId, "pending", correlationId);

            // Cache idempotency result
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                await _cache.SetAsync($"idempotency:{tenantId}:{idempotencyKey}", response, IdempotencyTtl, cancellationToken);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Ingestion failed for doctype {Doctype}", doctype);
            await AuditAsync(tenantId, "POST", $"/api/v1/ingest/{doctype}", 500, stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    public async Task<IngestionResponse> UpdateAsync(
        string doctype,
        string name,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var correlationId = GetCorrelationId();
        var jobId = UlidGenerator.Generate();

        if (!_doctypeValidator.IsAllowed(doctype))
        {
            throw new ArgumentException($"Doctype '{doctype}' is not in the allowed list.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await PublishEventAsync(
                doctype,
                "updated",
                payload,
                name,
                jobId,
                correlationId,
                tenantId,
                cancellationToken);

            var jobStatus = new JobStatusResponse(jobId, "pending", DateTimeOffset.UtcNow, null, null);
            await _cache.SetAsync($"job:{jobId}", jobStatus, JobTtl, cancellationToken);

            await AuditAsync(tenantId, "PUT", $"/api/v1/ingest/{doctype}/{name}", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            return new IngestionResponse(jobId, "pending", correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Update failed for doctype {Doctype} name {Name}", doctype, name);
            await AuditAsync(tenantId, "PUT", $"/api/v1/ingest/{doctype}/{name}", 500, stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    public async Task<IngestionResponse> DeleteAsync(
        string doctype,
        string name,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var correlationId = GetCorrelationId();
        var jobId = UlidGenerator.Generate();

        if (!_doctypeValidator.IsAllowed(doctype))
        {
            throw new ArgumentException($"Doctype '{doctype}' is not in the allowed list.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await PublishEventAsync(
                doctype,
                "deleted",
                JsonDocument.Parse("{}").RootElement,
                name,
                jobId,
                correlationId,
                tenantId,
                cancellationToken);

            var jobStatus = new JobStatusResponse(jobId, "pending", DateTimeOffset.UtcNow, null, null);
            await _cache.SetAsync($"job:{jobId}", jobStatus, JobTtl, cancellationToken);

            await AuditAsync(tenantId, "DELETE", $"/api/v1/ingest/{doctype}/{name}", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            return new IngestionResponse(jobId, "pending", correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Delete failed for doctype {Doctype} name {Name}", doctype, name);
            await AuditAsync(tenantId, "DELETE", $"/api/v1/ingest/{doctype}/{name}", 500, stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    public async Task<IngestionResponse> BatchIngestAsync(
        IReadOnlyList<BatchOperation> operations,
        CancellationToken cancellationToken)
    {
        if (operations.Count > 100)
        {
            throw new ArgumentException("Batch size cannot exceed 100 operations.");
        }

        var tenantId = GetTenantId();
        var correlationId = GetCorrelationId();
        var batchJobId = UlidGenerator.Generate();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var op in operations)
            {
                if (!_doctypeValidator.IsAllowed(op.Doctype))
                {
                    throw new ArgumentException($"Doctype '{op.Doctype}' is not in the allowed list.");
                }

                var jobId = UlidGenerator.Generate();
                await PublishEventAsync(
                    op.Doctype,
                    "created",
                    op.Payload,
                    op.Name,
                    jobId,
                    correlationId,
                    tenantId,
                    cancellationToken);
            }

            var jobStatus = new JobStatusResponse(batchJobId, "pending", DateTimeOffset.UtcNow, null, null);
            await _cache.SetAsync($"job:{batchJobId}", jobStatus, JobTtl, cancellationToken);

            await AuditAsync(tenantId, "POST", "/api/v1/ingest/batch", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            return new IngestionResponse(batchJobId, "pending", correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Batch ingestion failed");
            await AuditAsync(tenantId, "POST", "/api/v1/ingest/batch", 500, stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        return await _cache.GetAsync<JobStatusResponse>($"job:{jobId}", cancellationToken);
    }

    private async Task PublishEventAsync(
        string doctype,
        string action,
        JsonElement payload,
        string? name,
        string jobId,
        string correlationId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _rabbitMqOptions.Value;
        await using var connection = await _rabbitMqFactory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var envelope = new ErpEventEnvelope(
            jobId,
            $"erphub.ingestion.{doctype}.{action}",
            "ERPApiHub",
            correlationId,
            DateTimeOffset.UtcNow,
            "1",
            payload);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = jobId,
            CorrelationId = correlationId
        };

        await channel.BasicPublishAsync(
            options.ExchangeName,
            $"erphub.ingestion.{doctype}.{action}",
            false,
            properties,
            body,
            cancellationToken);

        _logger.LogInformation(
            "Published event to {Exchange} with routing key {RoutingKey} for job {JobId}",
            options.ExchangeName,
            $"erphub.ingestion.{doctype}.{action}",
            jobId);
    }

    private async Task AuditAsync(
        string tenantId,
        string method,
        string endpoint,
        int statusCode,
        long durationMs,
        CancellationToken cancellationToken)
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var systemId = _httpContextAccessor.HttpContext?.User.FindFirst("client_id")?.Value;

        var auditLog = new AuditLog
        {
            LogId = UlidGenerator.Generate(),
            TenantId = tenantId,
            SystemId = systemId,
            UserId = userId,
            Method = method,
            Endpoint = endpoint,
            StatusCode = statusCode,
            DurationMs = (int)durationMs,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private string GetTenantId()
    {
        return _httpContextAccessor.HttpContext?.User.FindFirst("BranchId")?.Value
            ?? throw new UnauthorizedAccessException("BranchId claim not found in JWT.");
    }

    private string GetCorrelationId()
    {
        return _httpContextAccessor.HttpContext?.Request.Headers["X-Request-ID"].FirstOrDefault()
            ?? UlidGenerator.Generate();
    }

    private string? GetIdempotencyKey()
    {
        return _httpContextAccessor.HttpContext?.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
    }
}
