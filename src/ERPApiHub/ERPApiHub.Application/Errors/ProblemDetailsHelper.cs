using System.Net;

namespace ERPApiHub.Application.Errors;

/// <summary>
/// Factory for creating RFC 7807 Problem Details responses.
/// Centralizes all error type URIs per FRD §12.2.
/// </summary>
public static class ProblemDetailsHelper
{
    private const string BaseUri = "https://api.hnhtravel.work/errors";

    public static ErpHubProblemDetails Validation(
        string detail,
        string? instance = null,
        string? requestId = null,
        List<FieldError>? errors = null) => new()
    {
        Type = $"{BaseUri}/validation",
        Title = "Validation Failed",
        Status = (int)HttpStatusCode.BadRequest,
        Detail = detail,
        Instance = instance,
        RequestId = requestId,
        Errors = errors
    };

    public static ErpHubProblemDetails Unauthorized(
        string detail = "Authentication required",
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/unauthorized",
        Title = "Unauthorized",
        Status = (int)HttpStatusCode.Unauthorized,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };

    public static ErpHubProblemDetails Forbidden(
        string detail = "Insufficient permissions",
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/forbidden",
        Title = "Forbidden",
        Status = (int)HttpStatusCode.Forbidden,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };

    public static ErpHubProblemDetails NotFound(
        string detail = "Resource not found",
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/not-found",
        Title = "Not Found",
        Status = (int)HttpStatusCode.NotFound,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };

    public static ErpHubProblemDetails Conflict(
        string detail = "Resource conflict",
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/conflict",
        Title = "Conflict",
        Status = (int)HttpStatusCode.Conflict,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };

    public static ErpHubProblemDetails RateLimited(
        string detail,
        int retryAfter,
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/rate-limited",
        Title = "Rate Limit Exceeded",
        Status = (int)HttpStatusCode.TooManyRequests,
        Detail = detail,
        Instance = instance,
        RequestId = requestId,
        RetryAfter = retryAfter
    };

    public static ErpHubProblemDetails ErpNextError(
        string detail,
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/erpnext-error",
        Title = "ERPNext Error",
        Status = (int)HttpStatusCode.BadGateway,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };

    public static ErpHubProblemDetails ErpNextTimeout(
        string detail = "ERPNext request timed out",
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/erpnext-timeout",
        Title = "ERPNext Timeout",
        Status = (int)HttpStatusCode.GatewayTimeout,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };

    public static ErpHubProblemDetails InternalError(
        string detail = "An unexpected error occurred",
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/internal-error",
        Title = "Internal Server Error",
        Status = (int)HttpStatusCode.InternalServerError,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };

    public static ErpHubProblemDetails PayloadTooLarge(
        string detail = "Request payload too large",
        string? instance = null,
        string? requestId = null) => new()
    {
        Type = $"{BaseUri}/payload-too-large",
        Title = "Payload Too Large",
        Status = 413,
        Detail = detail,
        Instance = instance,
        RequestId = requestId
    };
}