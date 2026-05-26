using System.Text.Json;
using ERPApiHub.Application.Ingestion;
using ERPApiHub.Infrastructure;
using ERPApiHub.Infrastructure.Data;
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

app.UseAuthentication();
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

app.MapPut("/api/v1/ingest/{doctype}/{name}", async (string doctype, string name, System.Text.Json.JsonElement payload, IngestionService service, HttpContext ctx, CancellationToken ct) =>
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

app.MapGet("/api/v1/ingest/status/{jobId}", async (string jobId, ERPApiHub.Infrastructure.Caching.IRedisCacheService cache, CancellationToken ct) =>
{
    var status = await cache.GetAsync<object>($"job:{jobId}", ct);
    return status is null ? Results.NotFound(new { error = "Job not found" }) : Results.Ok(status);
}).RequireAuthorization("api-hub:read");

app.MapGet("/api/v1/ingest/dlq", () =>
{
    // DLQ listing requires RabbitMQ Management API — placeholder
    return Results.Ok(new { message = "DLQ listing requires RabbitMQ Management API integration", items = Array.Empty<object>() });
}).RequireAuthorization("api-hub:admin");

app.MapPost("/api/v1/ingest/dlq/{id}/replay", (string id) =>
{
    // DLQ replay — placeholder
    return Results.Accepted(null, new { message = $"Replay requested for DLQ message {id}" });
}).RequireAuthorization("api-hub:admin");

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