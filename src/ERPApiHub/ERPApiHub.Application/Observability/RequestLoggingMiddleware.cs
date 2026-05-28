using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Observability;

/// <summary>
/// Request/response logging middleware with structured logging and duration tracking.
/// FRD ref: §15 (FR-OBS-001~003)
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestMethod = context.Request.Method;
        var requestPath = context.Request.Path;
        var tenantId = context.User?.FindFirst("BranchId")?.Value ?? "anonymous";
        var userId = context.User?.FindFirst("preferred_username")?.Value ?? "anonymous";

        // Attach request metadata to the activity (if OpenTelemetry is active)
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("http.request.method", requestMethod);
            activity.SetTag("http.request.path", requestPath);
            activity.SetTag("tenant.id", tenantId);
            activity.SetTag("user.id", userId);
        }

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;

            // Structured log
            _logger.LogInformation(
                "HTTP {Method} {Path} → {StatusCode} in {Duration:F1}ms [Tenant:{TenantId}, User:{UserId}]",
                requestMethod, requestPath, statusCode, durationMs, tenantId, userId);

            // Add to activity
            if (activity is not null)
            {
                activity.SetTag("http.response.status_code", statusCode);
                activity.SetTag("request.duration_ms", durationMs);
            }
        }
    }
}

/// <summary>
/// Custom metrics for ERP API Hub.
/// FRD ref: §15 (FR-OBS-003)
/// </summary>
public static class ErpHubMetrics
{
    public static readonly ActivitySource ActivitySource = new("ERPApiHub", "1.0.0");
    public const string MeterName = "ERPApiHub";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        RequestsTotalMetric,
        description: "Total HTTP requests.");

    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        RequestDurationMetric,
        unit: "s",
        description: "HTTP request duration in seconds.");

    public static readonly Counter<long> ErrorsTotal = Meter.CreateCounter<long>(
        ErrorsTotalMetric,
        description: "Total unhandled request errors.");

    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        CacheHitsMetric,
        description: "Total cache hits.");

    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        CacheMissesMetric,
        description: "Total cache misses.");

    public static readonly Counter<long> IngestionJobs = Meter.CreateCounter<long>(
        IngestionJobsMetric,
        description: "Total ingestion jobs by status.");

    public const string RequestsTotalMetric = "erp_api_hub.requests.total";
    public const string RequestDurationMetric = "erp_api_hub.request.duration";
    public const string ErrorsTotalMetric = "erp_api_hub.errors.total";
    public const string CacheHitsMetric = "erp_api_hub.cache.hits";
    public const string CacheMissesMetric = "erp_api_hub.cache.misses";
    public const string IngestionJobsMetric = "erp_api_hub.ingestion.jobs";
    public const string WebhookDeliveriesMetric = "erp_api_hub.webhook.deliveries";
    public const string RateLimitHitsMetric = "erp_api_hub.rate_limit.hits";
}
