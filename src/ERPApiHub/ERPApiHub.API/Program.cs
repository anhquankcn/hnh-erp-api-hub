using System.Text.Json;
using ERPApiHub.Application.Audit;
using ERPApiHub.Application.Compliance;
using ERPApiHub.Application.Configuration;
using ERPApiHub.Application.Errors;
using ERPApiHub.Application.Ingestion;
using ERPApiHub.Application.Observability;
using ERPApiHub.Application.Query;
using ERPApiHub.Application.RateLimiting;
using ERPApiHub.Application.Webhooks;
using ERPApiHub.Infrastructure;
using ERPApiHub.Infrastructure.Caching;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.ErpNext;
using ERPApiHub.Infrastructure.Health;
using ERPApiHub.Infrastructure.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8008");
builder.Services.AddOpenApi();
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

// Application layer services
builder.Services.AddSingleton<AllowedDoctypeValidator>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<QueryService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<PiiMaskingService>();
builder.Services.AddScoped<WebhookSignatureService>();
builder.Services.AddScoped<WebhookSubscriptionService>();
builder.Services.AddScoped<WebhookDeliveryService>();
builder.Services.AddHttpClient("WebhookDelivery");

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
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("InternalGateway");
}

// S3-005: Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// S3-004: Global exception handler (RFC 7807)
app.UseMiddleware<ProblemDetailsMiddleware>();

app.UseAuthentication();

// S3-001: Rate limiting middleware (after auth so context.User is populated)
app.UseMiddleware<RateLimitMiddleware>();

app.UseAuthorization();

// Root & health
app.MapGet("/", () => Results.Ok(new { service = "erp-api-hub", status = "running" }))
    .AllowAnonymous();
app.MapGet("/v1/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

// ─── Ingestion Endpoints (S2-001, S2-002, S2-008) ───

app.MapPost("/api/v1/ingest/{doctype}", async (string doctype, IngestionRequest request, IngestionService service, HttpContext ctx, CancellationToken ct) =>
{
    var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
    var fullRequest = request with { Doctype = doctype, IdempotencyKey = idempotencyKey };
    var response = await service.IngestAsync(fullRequest, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
}).RequireAuthorization("api-hub:write");

app.MapPut("/api/v1/ingest/{doctype}/{name}", async (string doctype, string name, JsonElement payload, IngestionService service, HttpContext ctx, CancellationToken ct) =>
{
    var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
    var response = await service.UpdateAsync(doctype, name, payload, idempotencyKey, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
}).RequireAuthorization("api-hub:write");

app.MapDelete("/api/v1/ingest/{doctype}/{name}", async (string doctype, string name, IngestionService service, HttpContext ctx, CancellationToken ct) =>
{
    var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
    var response = await service.DeleteAsync(doctype, name, idempotencyKey, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
}).RequireAuthorization("api-hub:write");

app.MapPost("/api/v1/ingest/batch", async (List<IngestionRequest> operations, IngestionService service, CancellationToken ct) =>
{
    var response = await service.BatchIngestAsync(operations, ct);
    return Results.Accepted($"/api/v1/ingest/status/{response.JobId}", response);
}).RequireAuthorization("api-hub:write");

// ─── Ingestion Status & DLQ (S2-003) ───

app.MapGet("/api/v1/ingest/status/{jobId}", async (string jobId, IRedisCacheService cache, CancellationToken ct) =>
{
    var status = await cache.GetAsync<object>($"job:{jobId}", ct);
    return status is null ? Results.NotFound(new { error = "Job not found" }) : Results.Ok(status);
}).RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/ingest/dlq", () =>
{
    return Results.Ok(new { message = "DLQ listing requires RabbitMQ Management API integration", items = Array.Empty<object>() });
}).RequireAuthorization("api-hub:admin");

app.MapPost("/api/v1/ingest/dlq/{id}/replay", (string id) =>
{
    return Results.Accepted(null, new { message = $"Replay requested for DLQ message {id}" });
}).RequireAuthorization("api-hub:admin");

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
}).RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/query/{doctype}/{name}", async (string doctype, string name, QueryService queryService, CancellationToken ct) =>
{
    var result = await queryService.GetAsync(doctype, name, ct);
    return Results.Ok(result);
}).RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/query/{doctype}/count", async (string doctype, QueryService queryService, CancellationToken ct) =>
{
    var result = await queryService.CountAsync(doctype, ct);
    return Results.Ok(result);
}).RequireAuthorization("api-hub:read");

app.MapDelete("/api/v1/cache/{doctype}", async (string doctype, QueryService queryService, CancellationToken ct) =>
{
    await queryService.PurgeCacheAsync(doctype, ct);
    return Results.Ok(new { message = $"Cache purged for {doctype}" });
}).RequireAuthorization("api-hub:admin");

// ─── Audit Endpoints (S2-007) ───

app.MapGet("/api/v1/audit/logs", async (string? tenantId, string? userId, string? endpoint, int? statusCode, string? fromDate, string? toDate, int? page, int? pageSize, AuditService auditService, CancellationToken ct) =>
{
    DateTimeOffset? from = fromDate is not null ? DateTimeOffset.Parse(fromDate) : null;
    DateTimeOffset? to = toDate is not null ? DateTimeOffset.Parse(toDate) : null;

    var result = await auditService.QueryLogsAsync(tenantId, userId, endpoint, statusCode, from, to, page ?? 1, pageSize ?? 20, ct);
    return Results.Ok(result);
}).RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/audit/logs/export", async (string? tenantId, string? fromDate, string? toDate, AuditService auditService, CancellationToken ct) =>
{
    DateTimeOffset? from = fromDate is not null ? DateTimeOffset.Parse(fromDate) : null;
    DateTimeOffset? to = toDate is not null ? DateTimeOffset.Parse(toDate) : null;

    var csv = await auditService.ExportLogsAsCsvAsync(tenantId, from, to, ct);
    return Results.Text(csv, "text/csv", Encoding.UTF8);
}).RequireAuthorization("api-hub:admin");

// ─── Webhook Endpoints (S2-006) ───

app.MapPost("/api/v1/webhooks/subscriptions", async (CreateWebhookSubscriptionRequest req, WebhookSubscriptionService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var sub = await service.CreateAsync(req.SystemId, req.EventTypes, req.WebhookUrl, req.Secret, tenantId, ct);
    return Results.Created($"/api/v1/webhooks/subscriptions/{sub.SubscriptionId}", sub);
}).RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/webhooks/subscriptions", async (WebhookSubscriptionService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var subs = await service.ListByTenantAsync(tenantId, ct);
    return Results.Ok(subs);
}).RequireAuthorization("api-hub:admin");

app.MapPut("/api/v1/webhooks/subscriptions/{id}", async (string id, UpdateWebhookSubscriptionRequest req, WebhookSubscriptionService service, CancellationToken ct) =>
{
    var sub = await service.UpdateAsync(id, req.EventTypes, req.WebhookUrl, req.Secret, ct);
    return Results.Ok(sub);
}).RequireAuthorization("api-hub:admin");

app.MapDelete("/api/v1/webhooks/subscriptions/{id}", async (string id, WebhookSubscriptionService service, CancellationToken ct) =>
{
    await service.DeleteAsync(id, ct);
    return Results.NoContent();
}).RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/webhooks/deliveries/{subscriptionId}", async (string subscriptionId, WebhookDeliveryService service, CancellationToken ct) =>
{
    var deliveries = await service.ListDeliveriesAsync(subscriptionId, ct);
    return Results.Ok(deliveries);
}).RequireAuthorization("api-hub:admin");

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

    var (isValid, validationError) = service.ValidateEnvelope(envelope);
    if (!isValid)
    {
        return Results.BadRequest(new { error = validationError });
    }

    await service.ProcessEventAsync(envelope, ct);
    return Results.Accepted(null, new { status = "accepted" });
}).AllowAnonymous();

// ─── External System Configuration Endpoints (S3-002) ───

app.MapPost("/api/v1/systems", async (CreateExternalSystemRequest req, ExternalSystemService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var system = await service.CreateAsync(req, tenantId, ct);
    return Results.Created($"/api/v1/systems/{system.SystemId}", system);
}).RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/systems", async (int? page, int? pageSize, ExternalSystemService service, HttpContext ctx, CancellationToken ct) =>
{
    var tenantId = ctx.User.FindFirst("BranchId")?.Value ?? "unknown";
    var (systems, total) = await service.ListAsync(tenantId, page ?? 1, pageSize ?? 20, ct);
    return Results.Ok(new { items = systems, total });
}).RequireAuthorization("api-hub:admin");

app.MapGet("/api/v1/systems/{systemId}", async (string systemId, ExternalSystemService service, CancellationToken ct) =>
{
    var system = await service.GetByIdAsync(systemId, ct);
    return system is null ? Results.NotFound(new { error = "System not found" }) : Results.Ok(system);
}).RequireAuthorization("api-hub:read");

app.MapPut("/api/v1/systems/{systemId}", async (string systemId, UpdateExternalSystemRequest req, ExternalSystemService service, CancellationToken ct) =>
{
    var system = await service.UpdateAsync(systemId, req, ct);
    return Results.Ok(system);
}).RequireAuthorization("api-hub:admin");

app.MapDelete("/api/v1/systems/{systemId}", async (string systemId, ExternalSystemService service, CancellationToken ct) =>
{
    await service.DeleteAsync(systemId, ct);
    return Results.NoContent();
}).RequireAuthorization("api-hub:admin");

app.MapPost("/api/v1/systems/{systemId}/rotate-key", async (string systemId, RotateApiKeyRequest req, ExternalSystemService service, CancellationToken ct) =>
{
    var mapping = await service.RotateApiKeyAsync(systemId, req.NewApiKey, req.NewApiSecret, ct);
    return Results.Ok(new { mapping_id = mapping.MappingId, created_at = mapping.CreatedAt });
}).RequireAuthorization("api-hub:admin");

// ─── Vietnam Compliance Endpoints (S3-006) ───

app.MapPost("/api/v1/compliance/tax-id/validate", (TaxIdValidationRequest req, VietnamComplianceService service) =>
{
    var (isValid, error) = service.ValidateTaxId(req.TaxId);
    return Results.Ok(new { valid = isValid, error });
}).RequireAuthorization("api-hub:read");

app.MapPost("/api/v1/compliance/e-invoice/validate", (EInvoiceRequest req, VietnamComplianceService service) =>
{
    var (isValid, errors) = service.ValidateEInvoice(req);
    return Results.Ok(new { valid = isValid, errors });
}).RequireAuthorization("api-hub:read");

app.MapPost("/api/v1/compliance/invoice-template/validate", (InvoiceTemplateValidationRequest req, VietnamComplianceService service) =>
{
    var (isValid, error) = service.ValidateInvoiceTemplate(req.Template);
    return Results.Ok(new { valid = isValid, error });
}).RequireAuthorization("api-hub:read");

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

public sealed record InvoiceTemplateValidationRequest
{
    public string Template { get; init; } = string.Empty;
}

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
