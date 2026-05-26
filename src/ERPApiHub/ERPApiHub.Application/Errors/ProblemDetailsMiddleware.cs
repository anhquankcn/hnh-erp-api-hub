using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Errors;

/// <summary>
/// Global exception handler middleware that maps unhandled exceptions to RFC 7807 Problem Details.
/// FRD ref: §12.2
/// </summary>
public sealed class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;

    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await WriteProblemResponseAsync(context, ex);
        }
    }

    private static async Task WriteProblemResponseAsync(HttpContext context, Exception exception)
    {
        var (problem, statusCode) = MapExceptionToProblemDetails(exception, context);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }

    private static (ErpHubProblemDetails Problem, int StatusCode) MapExceptionToProblemDetails(
        Exception exception, HttpContext context)
    {
        var path = context.Request.Path.Value;
        var requestId = context.TraceIdentifier;

        return exception switch
        {
            UnauthorizedAccessException => (
                ProblemDetailsHelper.Unauthorized(
                    exception.Message,
                    path,
                    requestId),
                StatusCodes.Status401Unauthorized),

            InvalidOperationException when exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) => (
                ProblemDetailsHelper.NotFound(
                    exception.Message,
                    path,
                    requestId),
                StatusCodes.Status404NotFound),

            InvalidOperationException => (
                ProblemDetailsHelper.Forbidden(
                    exception.Message,
                    path,
                    requestId),
                StatusCodes.Status403Forbidden),

            ArgumentException => (
                ProblemDetailsHelper.Validation(
                    exception.Message,
                    path,
                    requestId),
                StatusCodes.Status400BadRequest),

            TimeoutException => (
                ProblemDetailsHelper.ErpNextTimeout(
                    exception.Message ?? "Request timed out",
                    path,
                    requestId),
                StatusCodes.Status504GatewayTimeout),

            HttpRequestException httpEx => (
                ProblemDetailsHelper.ErpNextError(
                    httpEx.Message,
                    path,
                    requestId),
                StatusCodes.Status502BadGateway),

            _ => (
                ProblemDetailsHelper.InternalError(
                    "An unexpected error occurred",
                    path,
                    requestId),
                StatusCodes.Status500InternalServerError)
        };
    }
}