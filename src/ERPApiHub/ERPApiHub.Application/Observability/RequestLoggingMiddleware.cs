using System.Diagnostics;
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

    // Counter names for OpenTelemetry
    public const string RequestsTotalMetric = "erphub.requests.total";
    public const string RequestDurationMetric = "erphub.request.duration";
    public const string IngestionTotalMetric = "erphub.ingestion.total";
    public const string IngestionErrorsMetric = "erphub.ingestion.errors";
    public const string WebhookDeliveriesMetric = "erphub.webhook.deliveries";
    public const string RateLimitHitsMetric = "erphub.rate_limit.hits";
    public const string CacheHitRatioMetric = "erphub.cache.hit_ratio";
}