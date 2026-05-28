using System.Text;
using System.Text.Json;
using ERPApiHub.API.Services.Jobs;
using ERPApiHub.API.Health;
using ERPApiHub.API.Services.Caching;
using ERPApiHub.Application.Audit;
using ERPApiHub.Application.Compliance;
using ERPApiHub.Application.Configuration;
using ERPApiHub.Application.Errors;
using ERPApiHub.Application.Ingestion;
using ERPApiHub.Application.Observability;
using ERPApiHub.Application.Query;
using ERPApiHub.Application.RateLimiting;
using ERPApiHub.Application.Webhooks;
using ERPApiHub.API.Middleware;
using ERPApiHub.Infrastructure;
using ERPApiHub.Infrastructure.Caching;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.ErpNext;
using ERPApiHub.Infrastructure.Security;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Redis.StackExchange;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ApplicationCacheService = ERPApiHub.Application.Abstractions.ICacheService;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8008");
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/json",
        "application/problem+json"
    ]);
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("InternalGateway", policy =>
    {
        policy
            .WithOrigins("http://localhost:8888")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddErpHubInfrastructure(builder.Configuration);
builder.Services.AddKeycloakJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(ErpHubMetrics.MeterName)
            .AddPrometheusExporter();
    });

// S4-001: Multi-level caching
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));
builder.Services.AddSingleton<CacheService>();
builder.Services.AddSingleton<ApplicationCacheService>(sp => sp.GetRequiredService<CacheService>());
builder.Services.AddScoped<CacheInvalidationService>();

// S4-004: Hangfire background jobs
builder.Services.Configure<JobOptions>(builder.Configuration.GetSection(JobOptions.SectionName));
builder.Services.AddScoped<CacheWarmingJob>();
builder.Services.AddScoped<CacheEvictionJob>();
builder.Services.AddScoped<HealthCheckAggregationJob>();
var jobOptionsForHangfire = builder.Configuration.GetSection(JobOptions.SectionName).Get<JobOptions>();
builder.Services.AddHangfire(configuration =>
{
    configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseRedisStorage(BuildRedisConnectionString(builder.Configuration), new RedisStorageOptions
        {
            Prefix = jobOptionsForHangfire?.RedisPrefix ?? "erphub:hangfire:"
        });
});

if (builder.Configuration.GetSection(JobOptions.SectionName).Get<JobOptions>()?.Enabled != false)
{
    builder.Services.AddHangfireServer();
}

// Application layer services
builder.Services.AddSingleton<AllowedDoctypeValidator>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<InvoiceDeletionGuard>();
builder.Services.AddScoped<QueryService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<PiiMaskingService>();
builder.Services.AddScoped<WebhookSignatureService>();
builder.Services.AddScoped<WebhookSubscriptionService>();
builder.Services.AddScoped<WebhookDeliveryService>();
builder.Services.AddHttpClient("WebhookDelivery");

// S6-002: PDPA REST endpoints
builder.Services.AddScoped<ConsentService>();

// S4-002: Token Lifecycle
builder.Services.AddScoped<ERPApiHub.Application.Auth.TokenService>();
builder.Services.AddHttpClient("KeycloakToken");

// S4-005: DLQ Management
builder.Services.AddScoped<DlqManagementService>();

// S3-001: Rate Limiting
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.AddScoped<RateLimitService>();

// S3-002: External System Configuration
builder.Services.AddScoped<ExternalSystemService>();

// S3-003: ERPNext Event Ingestion
builder.Services.Configure<ErpNextEventOptions>(builder.Configuration.GetSection(ErpNextEventOptions.SectionName));
builder.Services.AddScoped<ErpNextEventIngestionService>();

// S3-006: Vietnam Compliance
builder.Services.AddSingleton<VietnamComplianceService>();

// S3-005: Observability
if (builder.Configuration.GetValue<bool>("OpenTelemetry:Enabled"))
{
    builder.Services.AddErpHubObservability(builder.Configuration);
}

// Authorization policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("api-hub:write", policy =>
        policy.RequireRole("erp-hub-write", "SuperAdmin"))
    .AddPolicy("api-hub:read", policy =>
        policy.RequireRole("erp-hub-read", "SuperAdmin"))
    .AddPolicy("api-hub:admin", policy =>
        policy.RequireRole("erp-hub-admin", "SuperAdmin"));

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ErpHubDbContext>("postgres", tags: ["ready", "startup"])
    .AddCheck<RedisHealthCheck>("redis-cache", tags: ["ready", "startup"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"])
    .AddCheck<ErpNextHealthCheck>("erpnext", tags: ["ready"]);

// S5-005: API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddApiExplorer();

var app = builder.Build();

var apiVersionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .ReportApiVersions()
    .Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("InternalGateway");
}

app.UseResponseCompression();

// S3-005: Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// S3-004: Global exception handler (RFC 7807)
app.UseMiddleware<ProblemDetailsMiddleware>();

app.UseAuthentication();

// S3-001: Rate limiting middleware (after auth so context.User is populated)
app.UseMiddleware<RateLimitMiddleware>();

// S5-007: Prometheus metrics collection
app.UseMiddleware<MetricsMiddleware>();

app.UseAuthorization();

var jobOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobOptions>>().Value;
if (jobOptions.Enabled)
{
    if (jobOptions.DashboardEnabled)
    {
        app.UseHangfireDashboard(jobOptions.DashboardPath, new DashboardOptions
        {
            Authorization = [new AdminRoleDashboardAuthorizationFilter()]
        });
    }

    RegisterRecurringJobs(jobOptions);
}

app.MapControllers();

// ─── Auth Endpoints (S4-002) ───

app.MapPost("/api/v1/auth/refresh", async (RefreshTokenRequest req, ERPApiHub.Application.Auth.TokenService service, CancellationToken ct) =>
{
    var result = await service.RefreshTokenAsync(req.RefreshToken, ct);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .AllowAnonymous();

app.MapPost("/api/v1/auth/revoke", async (HttpContext ctx, RevokeTokenRequest req, ERPApiHub.Application.Auth.TokenService service, CancellationToken ct) =>
{
    // Require authentication — only allow revoking the currently validated token
    if (!ctx.User.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();

    var jti = ctx.User.FindFirst("jti")?.Value;
    if (string.IsNullOrEmpty(jti))
    {
        return Results.BadRequest(new { error = "Token missing jti claim" });
    }

    await service.RevokeTokenAsync(jti, TimeSpan.FromHours(1), ct);
    return Results.Ok(new { message = "Token revoked" });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization();

app.MapPost("/api/v1/auth/verify", (VerifyTokenRequest req, ERPApiHub.Application.Auth.TokenService service) =>
{
    var verify = service.ValidateAndDecodeToken(req.Token);
    return verify is not null ? Results.Ok(verify) : Results.Unauthorized();
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .AllowAnonymous();

// Root & health
app.MapGet("/", () => Results.Ok(new { service = "erp-api-hub", status = "running" }))
    .AllowAnonymous();

app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

// S5-005: Version Discovery
app.MapGet("/versions", () => Results.Ok(new
{
    versions = new[] { "1.0", "2.0" },
    deprecated = new[] { "1.0" },
    sunsetDate = "2026-12-31T00:00:00Z"
})).AllowAnonymous();

// ─── Versioned Health Endpoints ───

// v1 Health — basic (deprecated, gets Sunset header)
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok", version = "1.0" }))
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .AllowAnonymous();

// v2 Health — enhanced with uptime
app.MapGet("/api/v2/health", () =>
{
    var uptime = DateTimeOffset.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime;
    return Results.Ok(new
    {
        status = "ok",
        version = "2.0",
        uptime = uptime.TotalSeconds,
        uptimeFormatted = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"
    });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(2, 0)
    .AllowAnonymous();

// v2 Detailed Health — full check results
app.MapGet("/api/v2/health/detailed", async (HealthCheckService healthCheckService, CancellationToken ct) =>
{
    var report = await healthCheckService.CheckHealthAsync(ct);
    return Results.Ok(new
    {
        status = report.Status.ToString(),
        version = "2.0",
        checkedAt = DateTimeOffset.UtcNow,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration = e.Value.Duration.TotalMilliseconds
        })
    });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(2, 0)
    .AllowAnonymous();

// Sunset header middleware for v1 responses
app.Use(async (context, next) =>
{
    await next();

    var hasV1Metadata = context.GetEndpoint()?.Metadata.GetMetadata<ApiVersionAttribute>()?.Versions
        ?.Any(v => v.MajorVersion == 1 && v.MinorVersion == 0) == true;
    var isV1Path = context.Request.Path.StartsWithSegments("/api/v1");

    if (context.Response.StatusCode is >= 200 and < 300 && (hasV1Metadata || isV1Path))
    {
        context.Response.Headers.Append("Sunset", "Sat, 31 Dec 2026 00:00:00 GMT");
        context.Response.Headers.Append("Deprecation", "true");
    }
});

// ─── Ingestion Endpoints (S2-001, S2-002, S2-008) ───

app.MapPost("/api/v1/ingest/{doctype}", async (string doctype, IngestionRequest request, IngestionService service, HttpContext ctx, CancellationToken ct) =>
{
    var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
    var fullRequest = request with { Doctype = doctype, IdempotencyKey = idempotencyKey };
    var response = await service.IngestAsync(fullRequest.Doctype, fullRequest.Payload, fullRequest.Name, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:write");

app.MapPut("/api/v1/ingest/{doctype}/{name}", async (string doctype, string name, JsonElement payload, IngestionService service, HttpContext ctx, CancellationToken ct) =>
{
    var response = await service.UpdateAsync(doctype, name, payload, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:write");

app.MapDelete("/api/v1/ingest/{doctype}/{name}", async (string doctype, string name, IngestionService service, HttpContext ctx, CancellationToken ct) =>
{
    var response = await service.DeleteAsync(doctype, name, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:write");

app.MapPost("/api/v1/ingest/batch", async (List<IngestionRequest> operations, IngestionService service, CancellationToken ct) =>
{
    var batchOperations = operations
        .Select(op => new BatchOperation(op.Doctype, op.Payload, op.Name))
        .ToList();
    var response = await service.BatchIngestAsync(batchOperations, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:write");

// ─── Ingestion Status & DLQ (S2-003) ───

app.MapGet("/api/v1/ingest/status/{jobId}", async (string jobId, IRedisCacheService cache, CancellationToken ct) =>
{
    var status = await cache.GetAsync<object>($"job:{jobId}", ct);
    return status is null ? Results.NotFound(new { error = "Job not found" }) : Results.Ok(status);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/ingest/dlq", async (int? page, int? pageSize, DlqManagementService dlqService, CancellationToken ct) =>
{
    var normalizedPage = Math.Max(page ?? 1, 1);
    var normalizedPageSize = Math.Clamp(pageSize ?? 50, 1, 100);
    var (items, total) = await dlqService.GetDeadLettersAsync(normalizedPage, normalizedPageSize, ct);

    return Results.Ok(new
    {
        items,
        total,
        page = normalizedPage,
        pageSize = normalizedPageSize
    });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapPost("/api/v1/ingest/dlq/{id}/replay", async (string id, DlqManagementService dlqService, CancellationToken ct) =>
{
    await dlqService.ReplayAsync(id, ct);
    return Results.Accepted(null, new { message = $"Replay requested for DLQ message {id}" });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

// ─── Query Endpoints (S2-004) ───

app.MapGet("/api/v1/query/{doctype}", async (string doctype, int? page, int? pageSize, string? filters, string? orderBy, string? fields, QueryService queryService, CancellationToken ct) =>
{
    var request = new QueryRequest
    {
        Doctype = doctype,
        Page = page ?? 1,
        PageSize = Math.Min(pageSize ?? 20, 100),
        Filters = filters,
        OrderBy = orderBy,
        Fields = fields
    };

    var response = await queryService.ListAsync(request, ct);
    return Results.Ok(response);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/query/{doctype}/stream", (string doctype, int? pageSize, int? maxPages, string? filters, string? orderBy, string? fields, QueryService queryService, CancellationToken ct) =>
{
    var normalizedPageSize = Math.Clamp(pageSize ?? 100, 1, 500);
    var normalizedMaxPages = Math.Clamp(maxPages ?? 100, 1, 1000);

    return StreamQueryAsync(doctype, normalizedPageSize, normalizedMaxPages, filters, orderBy, fields, queryService, ct);
}).RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/query/{doctype}/{name}", async (string doctype, string name, QueryService queryService, CancellationToken ct) =>
{
    var result = await queryService.GetAsync(doctype, name, ct);
    return Results.Ok(result);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/query/{doctype}/count", async (string doctype, QueryService queryService, CancellationToken ct) =>
{
    var result = await queryService.CountAsync(doctype, ct);
    return Results.Ok(result);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

app.MapDelete("/api/v1/cache/{doctype}", async (string doctype, QueryService queryService, CancellationToken ct) =>
{
    await queryService.PurgeCacheAsync(doctype, ct);
    return Results.Ok(new { message = $"Cache purged for {doctype}" });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

// ─── Audit Endpoints (S2-007) ───

app.MapGet("/api/v1/audit/logs", async (string? tenantId, string? userId, string? endpoint, int? statusCode, string? fromDate, string? toDate, int? page, int? pageSize, AuditService auditService, CancellationToken ct) =>
{
    DateTimeOffset? from = fromDate is not null ? DateTimeOffset.Parse(fromDate) : null;
    DateTimeOffset? to = toDate is not null ? DateTimeOffset.Parse(toDate) : null;

    var result = await auditService.QueryLogsAsync(tenantId, userId, endpoint, statusCode, from, to, page ?? 1, pageSize ?? 20, ct);
    return Results.Ok(result);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/audit/logs/export", async (string? tenantId, string? fromDate, string? toDate, AuditService auditService, CancellationToken ct) =>
{
    DateTimeOffset? from = fromDate is not null ? DateTimeOffset.Parse(fromDate) : null;
    DateTimeOffset? to = toDate is not null ? DateTimeOffset.Parse(toDate) : null;

    var csv = await auditService.ExportLogsAsCsvAsync(tenantId, from, to, ct);
    return Results.Text(csv, "text/csv", Encoding.UTF8);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

// ─── Webhook Endpoints (S2-006) ───

app.MapPost("/api/v1/webhooks/subscriptions", async (CreateWebhookSubscriptionRequest req, WebhookSubscriptionService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var sub = await service.CreateAsync(req.SystemId, req.EventTypes, req.WebhookUrl, req.Secret, tenantId, ct);
    return Results.Created($"/api/v1/webhooks/subscriptions/{sub.SubscriptionId}", sub);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/webhooks/subscriptions", async (WebhookSubscriptionService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var subs = await service.ListByTenantAsync(tenantId, ct);
    return Results.Ok(subs);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapPut("/api/v1/webhooks/subscriptions/{id}", async (string id, UpdateWebhookSubscriptionRequest req, WebhookSubscriptionService service, CancellationToken ct) =>
{
    var sub = await service.UpdateAsync(id, req.EventTypes, req.WebhookUrl, req.Secret, ct);
    return Results.Ok(sub);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapDelete("/api/v1/webhooks/subscriptions/{id}", async (string id, WebhookSubscriptionService service, CancellationToken ct) =>
{
    await service.DeleteAsync(id, ct);
    return Results.NoContent();
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/webhooks/deliveries/{subscriptionId}", async (string subscriptionId, WebhookDeliveryService service, CancellationToken ct) =>
{
    var deliveries = await service.ListDeliveriesAsync(subscriptionId, ct);
    return Results.Ok(deliveries);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

// ─── ERPNext Event Ingestion Endpoint (S3-003) ───

app.MapPost("/internal/v1/events/ingest", async (HttpContext ctx, ErpNextEventIngestionService service, CancellationToken ct) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var rawBody = await reader.ReadToEndAsync(ct);
    var signatureHeader = ctx.Request.Headers["X-ERP-Hub-Signature-256"].FirstOrDefault() ?? string.Empty;

    if (!service.ValidateSignature(rawBody, signatureHeader))
    {
        return Results.Unauthorized();
    }

    System.Text.Json.JsonElement envelope;
    try
    {
        var doc = System.Text.Json.JsonDocument.Parse(rawBody);
        envelope = doc.RootElement.Clone();
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { error = "Invalid JSON payload." });
    }

    var eventType = envelope.TryGetProperty("eventType", out var eventTypeElement)
        && eventTypeElement.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(eventTypeElement.GetString())
            ? eventTypeElement.GetString()!
            : "erpnext.event";

    await service.IngestEventAsync(eventType, rawBody, null, ct);
    return Results.Accepted(null, new { status = "accepted" });
}).AllowAnonymous();

// ─── External System Configuration Endpoints (S3-002) ───

app.MapPost("/api/v1/systems", async (CreateExternalSystemRequest req, ExternalSystemService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var system = await service.CreateAsync(req, tenantId, ct);
    return Results.Created($"/api/v1/systems/{system.SystemId}", system);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/systems", async (int? page, int? pageSize, ExternalSystemService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var (systems, total) = await service.ListAsync(tenantId, page ?? 1, pageSize ?? 20, ct);
    return Results.Ok(new { items = systems, total });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/systems/{systemId}", async (string systemId, ExternalSystemService service, CancellationToken ct) =>
{
    var system = await service.GetByIdAsync(systemId, ct);
    return system is null ? Results.NotFound(new { error = "System not found" }) : Results.Ok(system);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

app.MapPut("/api/v1/systems/{systemId}", async (string systemId, UpdateExternalSystemRequest req, ExternalSystemService service, CancellationToken ct) =>
{
    var system = await service.UpdateAsync(systemId, req, ct);
    return Results.Ok(system);
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapDelete("/api/v1/systems/{systemId}", async (string systemId, ExternalSystemService service, CancellationToken ct) =>
{
    await service.DeleteAsync(systemId, ct);
    return Results.NoContent();
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

app.MapPost("/api/v1/systems/{systemId}/rotate-key", async (string systemId, RotateApiKeyRequest req, ExternalSystemService service, CancellationToken ct) =>
{
    var mapping = await service.RotateApiKeyAsync(systemId, req.NewApiKey, req.NewApiSecret, ct);
    return Results.Ok(new { mapping_id = mapping.MappingId, created_at = mapping.CreatedAt });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:admin");

// ─── Vietnam Compliance Endpoints (S3-006) ───

app.MapPost("/api/v1/compliance/tax-id/validate", (TaxIdValidationRequest req, VietnamComplianceService service) =>
{
    var (isValid, error) = service.ValidateTaxId(req.TaxId);
    return Results.Ok(new { valid = isValid, error });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

app.MapPost("/api/v1/compliance/e-invoice/validate", (EInvoiceRequest req, VietnamComplianceService service) =>
{
    var (isValid, errors) = service.ValidateEInvoice(req);
    return Results.Ok(new { valid = isValid, errors });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

app.MapPost("/api/v1/compliance/invoice-template/validate", (InvoiceTemplateValidationRequest req, VietnamComplianceService service) =>
{
    var (isValid, error) = service.ValidateInvoiceTemplate(req.Template);
    return Results.Ok(new { valid = isValid, error });
})
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(1, 0)
    .RequireAuthorization("api-hub:read");

// ─── Health Checks ───

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthCheckResponseAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckResponseAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResponseWriter = WriteHealthCheckResponseAsync
}).AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckResponseAsync
}).AllowAnonymous();

app.Run();

static Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString())
    };

    return JsonSerializer.SerializeAsync(context.Response.Body, payload);
}

static async IAsyncEnumerable<JsonElement> StreamQueryAsync(
    string doctype,
    int pageSize,
    int maxPages,
    string? filters,
    string? orderBy,
    string? fields,
    QueryService queryService,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
{
    for (var page = 1; page <= maxPages; page++)
    {
        var response = await queryService.ListAsync(new QueryRequest
        {
            Doctype = doctype,
            Page = page,
            PageSize = pageSize,
            Filters = filters,
            OrderBy = orderBy,
            Fields = fields
        }, cancellationToken);

        if (response.Data.Count == 0)
        {
            yield break;
        }

        foreach (var item in response.Data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        if (response.Data.Count < pageSize)
        {
            yield break;
        }
    }
}

static void RegisterRecurringJobs(JobOptions options)
{
    RecurringJob.AddOrUpdate<CacheWarmingJob>(
        "s4-cache-warming",
        job => job.WarmAsync(),
        options.CacheWarmingCron);

    RecurringJob.AddOrUpdate<CacheEvictionJob>(
        "s4-cache-eviction",
        job => job.CleanupExpiredEntriesAsync(),
        options.CacheEvictionCron);

    RecurringJob.AddOrUpdate<HealthCheckAggregationJob>(
        "s4-health-check-aggregation",
        job => job.AggregateAsync(),
        options.HealthAggregationCron);
}

static string BuildRedisConnectionString(IConfiguration configuration)
{
    var redisConnectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379";
    var redisPassword = configuration["Redis:Password"];

    if (!string.IsNullOrWhiteSpace(redisPassword)
        && !redisConnectionString.Contains("password=", StringComparison.OrdinalIgnoreCase))
    {
        redisConnectionString = $"{redisConnectionString},password={redisPassword}";
    }

    if (!redisConnectionString.Contains("abortConnect=", StringComparison.OrdinalIgnoreCase))
    {
        redisConnectionString = $"{redisConnectionString},abortConnect=false";
    }

    return redisConnectionString;
}

sealed class AdminRoleDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true
            && (user.IsInRole("erp-hub-admin") || user.IsInRole("SuperAdmin"));
    }
}

// ─── Request DTOs ───

public sealed record CreateWebhookSubscriptionRequest
{
    public string SystemId { get; init; } = string.Empty;
    public string[] EventTypes { get; init; } = [];
    public string WebhookUrl { get; init; } = string.Empty;
    public string? Secret { get; init; }
}

public sealed record UpdateWebhookSubscriptionRequest
{
    public string[]? EventTypes { get; init; }
    public string? WebhookUrl { get; init; }
    public string? Secret { get; init; }
}

public sealed record RotateApiKeyRequest
{
    public string NewApiKey { get; init; } = string.Empty;
    public string NewApiSecret { get; init; } = string.Empty;
}

public sealed record TaxIdValidationRequest
{
    public string TaxId { get; init; } = string.Empty;
}

public sealed record RefreshTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record RevokeTokenRequest
{
    public string Token { get; init; } = string.Empty;
}

public sealed record VerifyTokenRequest
{
    public string Token { get; init; } = string.Empty;
}

public sealed record InvoiceTemplateValidationRequest
{
    public string Template { get; init; } = string.Empty;
}
