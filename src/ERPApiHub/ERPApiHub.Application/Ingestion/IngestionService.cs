using System.Security.Claims;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Exceptions;
using ERPApiHub.Application.Observability;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
    private readonly ICacheService _cache;
    private readonly IErpHubRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMessageBus _messageBus;
    private readonly InvoiceDeletionGuard _invoiceDeletionGuard;
    private readonly IErpNextClient _erpNextClient;
    private readonly ILogger<IngestionService> _logger;
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(24);
    private const string SalesInvoiceDoctype = "Sales Invoice";
    private const string IssuedStatus = "Issued";
    private const string CancelledStatus = "Cancelled";

    public IngestionService(
        IAllowedDoctypeValidator doctypeValidator,
        ICacheService cache,
        IErpHubRepository repository,
        IHttpContextAccessor httpContextAccessor,
        IMessageBus messageBus,
        InvoiceDeletionGuard invoiceDeletionGuard,
        IErpNextClient erpNextClient,
        ILogger<IngestionService> logger)
    {
        _doctypeValidator = doctypeValidator;
        _cache = cache;
        _repository = repository;
        _httpContextAccessor = httpContextAccessor;
        _messageBus = messageBus;
        _invoiceDeletionGuard = invoiceDeletionGuard;
        _erpNextClient = erpNextClient;
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
        var response = new IngestionResponse(jobId, "pending", correlationId);
        string? idempotencyCacheKey = null;
        var idempotencyClaimed = false;

        // Idempotency check
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            idempotencyCacheKey = $"idempotency:{tenantId}:{idempotencyKey}";
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

        if (!string.IsNullOrEmpty(name))
        {
            await ValidateInvoiceStatusChangeAsync(tenantId, doctype, name, payload, cancellationToken);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (idempotencyCacheKey is not null)
            {
                idempotencyClaimed = await _cache.TrySetAsync(idempotencyCacheKey, response, IdempotencyTtl, cancellationToken);
                if (!idempotencyClaimed)
                {
                    var cached = await _cache.GetAsync<IngestionResponse>(idempotencyCacheKey, cancellationToken);
                    if (cached is not null)
                    {
                        _logger.LogInformation("Idempotency cache hit for key {IdempotencyKey}", idempotencyKey);
                        return cached;
                    }

                    throw new InvalidOperationException("Idempotency key is already being processed.");
                }
            }

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
            RecordIngestionJob("queued");

            // Audit log
            await AuditAsync(tenantId, "POST", $"/api/v1/ingest/{doctype}", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            if (idempotencyClaimed && idempotencyCacheKey is not null)
            {
                await _cache.RemoveAsync(idempotencyCacheKey, cancellationToken);
            }

            stopwatch.Stop();
            _logger.LogError(ex, "Ingestion failed for doctype {Doctype}", doctype);
            RecordIngestionJob("failed");
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

        await ValidateInvoiceStatusChangeAsync(tenantId, doctype, name, payload, cancellationToken);

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
            RecordIngestionJob("queued");

            await AuditAsync(tenantId, "PUT", $"/api/v1/ingest/{doctype}/{name}", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            return new IngestionResponse(jobId, "pending", correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Update failed for doctype {Doctype} name {Name}", doctype, name);
            RecordIngestionJob("failed");
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

        if (string.Equals(doctype, SalesInvoiceDoctype, StringComparison.Ordinal))
        {
            var result = await _invoiceDeletionGuard.CanDeleteAsync(
                doctype,
                name,
                GetForceDelete(),
                GetUserRole(),
                cancellationToken);

            if (!result.CanDelete)
            {
                await AuditAsync(
                    tenantId,
                    "INVOICE_DELETE_BLOCKED",
                    $"/api/v1/ingest/{doctype}/{name}",
                    StatusCodes.Status409Conflict,
                    0,
                    cancellationToken);

                throw new InvoiceDeletionBlockedException(result.Reason ?? "Invoice deletion blocked");
            }

            if (result.RequiresAudit)
            {
                await AuditAsync(
                    tenantId,
                    "INVOICE_SOFT_DELETE",
                    $"/api/v1/ingest/{doctype}/{name}",
                    StatusCodes.Status202Accepted,
                    0,
                    cancellationToken);
            }
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
            RecordIngestionJob("queued");

            await AuditAsync(tenantId, "DELETE", $"/api/v1/ingest/{doctype}/{name}", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            return new IngestionResponse(jobId, "pending", correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Delete failed for doctype {Doctype} name {Name}", doctype, name);
            RecordIngestionJob("failed");
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
            RecordIngestionJob("queued");

            await AuditAsync(tenantId, "POST", "/api/v1/ingest/batch", 202, stopwatch.ElapsedMilliseconds, cancellationToken);

            return new IngestionResponse(batchJobId, "pending", correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Batch ingestion failed");
            RecordIngestionJob("failed");
            await AuditAsync(tenantId, "POST", "/api/v1/ingest/batch", 500, stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        return await _cache.GetAsync<JobStatusResponse>($"job:{jobId}", cancellationToken);
    }

    private static void RecordIngestionJob(string status)
    {
        ErpHubMetrics.IngestionJobs.Add(
            1,
            new KeyValuePair<string, object?>("status", status));
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
        var envelope = new ErpEventEnvelope(
            jobId,
            $"erphub.ingestion.{doctype}.{action}",
            "ERPApiHub",
            correlationId,
            DateTimeOffset.UtcNow,
            "1",
            payload);

        var routingKey = $"erphub.ingestion.{doctype}.{action}";
        await _messageBus.PublishAsync(string.Empty, routingKey, envelope, cancellationToken);

        _logger.LogInformation(
            "Published event with routing key {RoutingKey} for job {JobId}",
            routingKey,
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

        await _repository.CreateAuditLogAsync(auditLog, cancellationToken);
    }

    private async Task ValidateInvoiceStatusChangeAsync(
        string tenantId,
        string doctype,
        string name,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(doctype, SalesInvoiceDoctype, StringComparison.Ordinal)
            || !TryGetStringProperty(payload, "status", out var newStatus)
            || !string.Equals(newStatus, CancelledStatus, StringComparison.Ordinal))
        {
            return;
        }

        var existingInvoice = await _erpNextClient.GetAsync<JsonElement>(
            $"{SalesInvoiceDoctype}/{Uri.EscapeDataString(name)}",
            cancellationToken);
        if (!existingInvoice.IsSuccessStatusCode || existingInvoice.Data is null)
        {
            await AuditAsync(
                tenantId,
                "INVOICE_STATUS_CHANGE_BLOCKED",
                $"/api/v1/ingest/{doctype}/{name}",
                StatusCodes.Status409Conflict,
                0,
                cancellationToken);

            throw new InvoiceStatusChangeBlockedException("Invoice status could not be verified");
        }

        var currentStatus = ExtractStatus(existingInvoice.Data);
        if (currentStatus is null)
        {
            await AuditAsync(
                tenantId,
                "INVOICE_STATUS_CHANGE_BLOCKED",
                $"/api/v1/ingest/{doctype}/{name}",
                StatusCodes.Status409Conflict,
                0,
                cancellationToken);

            throw new InvoiceStatusChangeBlockedException("Invoice status could not be verified");
        }

        if (!string.Equals(currentStatus, IssuedStatus, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetStringProperty(payload, "reason", out var reason)
            || string.IsNullOrWhiteSpace(reason))
        {
            await AuditAsync(
                tenantId,
                "INVOICE_STATUS_CHANGE_BLOCKED",
                $"/api/v1/ingest/{doctype}/{name}",
                StatusCodes.Status409Conflict,
                0,
                cancellationToken);

            throw new InvoiceStatusChangeBlockedException(
                "Reason is required when cancelling an issued invoice");
        }

        await AuditAsync(
            tenantId,
            "INVOICE_STATUS_CHANGE",
            $"/api/v1/ingest/{doctype}/{name}",
            StatusCodes.Status202Accepted,
            0,
            cancellationToken);
    }

    private static bool TryGetStringProperty(JsonElement payload, string propertyName, out string? value)
    {
        value = null;
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static string? ExtractStatus(JsonElement invoice)
    {
        if (invoice.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (invoice.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String)
        {
            return status.GetString();
        }

        if (invoice.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("status", out var nestedStatus)
            && nestedStatus.ValueKind == JsonValueKind.String)
        {
            return nestedStatus.GetString();
        }

        return null;
    }

    private bool GetForceDelete()
    {
        var forceValue = _httpContextAccessor.HttpContext?.Request.Query["force"].FirstOrDefault();
        return bool.TryParse(forceValue, out var force) && force;
    }

    private string GetUserRole()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.IsInRole("admin") == true)
        {
            return "admin";
        }

        return user?.FindFirst(ClaimTypes.Role)?.Value
            ?? user?.FindFirst("role")?.Value
            ?? user?.FindFirst("roles")?.Value
            ?? string.Empty;
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
