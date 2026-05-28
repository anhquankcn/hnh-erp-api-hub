using System.Diagnostics;
using ERPApiHub.Application.Observability;
using Microsoft.AspNetCore.Routing;

namespace ERPApiHub.API.Middleware;

public sealed class MetricsMiddleware
{
    private readonly RequestDelegate _next;

    public MetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
            RecordRequest(context, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            ErpHubMetrics.ErrorsTotal.Add(
                1,
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name));

            RecordRequest(context, stopwatch.Elapsed, StatusCodes.Status500InternalServerError);
            throw;
        }
    }

    private static void RecordRequest(HttpContext context, TimeSpan duration, int? statusCode = null)
    {
        var endpoint = ResolveEndpoint(context);
        var status = statusCode ?? context.Response.StatusCode;

        ErpHubMetrics.RequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("method", context.Request.Method),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status_code", status.ToString()));

        ErpHubMetrics.RequestDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>("endpoint", endpoint));
    }

    private static string ResolveEndpoint(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint routeEndpoint
            && !string.IsNullOrWhiteSpace(routeEndpoint.RoutePattern.RawText))
        {
            return routeEndpoint.RoutePattern.RawText;
        }

        return context.Request.Path.HasValue ? context.Request.Path.Value! : "unknown";
    }
}
