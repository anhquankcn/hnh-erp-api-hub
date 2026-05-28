using ERPApiHub.API.DTOs.Audit;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Audit;
using ERPApiHub.Application.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers;

[ApiController]
[Route("api/v2/audit")]
[Authorize]
public sealed class AuditController(
    IAuditSearchService auditService,
    ICacheService cacheService) : ControllerBase
{
    private static readonly TimeSpan ExportRateLimitWindow = TimeSpan.FromMinutes(1);

    [HttpGet("search")]
    [Authorize(Policy = "api-hub:read")]
    [ProducesResponseType(typeof(AuditSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditSearchResponse>> Search(
        [FromQuery] AuditSearchRequest request,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateSearchRequest(request);
        if (validationProblem is not null)
        {
            return BadRequest(validationProblem);
        }

        var result = await auditService.SearchAsync(ToQuery(request), cancellationToken);
        return Ok(new AuditSearchResponse
        {
            Items = result.Items.Select(ToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        });
    }

    [HttpPost("export")]
    [Authorize(Policy = "api-hub:admin")]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Export(
        [FromBody] AuditExportRequest request,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateExportRequest(request);
        if (validationProblem is not null)
        {
            return BadRequest(validationProblem);
        }

        if (await IsExportRateLimitedAsync(cancellationToken))
        {
            return RateLimited("Audit export rate limit exceeded.");
        }

        var stream = await auditService.ExportAsync(ToQuery(request), cancellationToken);
        var format = NormalizeFormat(request.Format);
        var contentType = format == "json" ? "application/json" : "text/csv";
        var extension = format == "json" ? "json" : "csv";
        var fileName = $"audit-export-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}";

        return File(stream, contentType, fileName);
    }

    private ErpHubProblemDetails? ValidateSearchRequest(AuditSearchRequest request)
    {
        if (request.PageNumber <= 0 || request.PageSize <= 0 || request.PageSize > 500)
        {
            return ValidationProblem("PageNumber must be greater than 0 and PageSize must be between 1 and 500.");
        }

        return ValidateDateRange(request.FromDate, request.ToDate);
    }

    private ErpHubProblemDetails? ValidateExportRequest(AuditExportRequest request)
    {
        var format = NormalizeFormat(request.Format);
        if (format is not "csv" and not "json")
        {
            return ValidationProblem("Format must be either 'csv' or 'json'.");
        }

        return ValidateDateRange(request.FromDate, request.ToDate);
    }

    private ErpHubProblemDetails? ValidateDateRange(DateTimeOffset? fromDate, DateTimeOffset? toDate)
    {
        if (fromDate is not null && toDate is not null && fromDate > toDate)
        {
            return ValidationProblem("FromDate must be earlier than or equal to ToDate.");
        }

        return null;
    }

    private async Task<bool> IsExportRateLimitedAsync(CancellationToken cancellationToken)
    {
        var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"audit:export:ratelimit:{NormalizeKeyPart(GetUserKey())}:{window}";
        var count = await cacheService.IncrementAsync(key, cancellationToken);

        if (count == 1)
        {
            await cacheService.ExpireAsync(key, ExportRateLimitWindow, cancellationToken);
        }

        return count > 10;
    }

    private ObjectResult RateLimited(string detail)
    {
        Response.Headers["Retry-After"] = ((int)ExportRateLimitWindow.TotalSeconds).ToString();
        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            ProblemDetailsHelper.RateLimited(
                detail,
                (int)ExportRateLimitWindow.TotalSeconds,
                HttpContext.Request.Path.ToString(),
                HttpContext.TraceIdentifier));
    }

    private ErpHubProblemDetails ValidationProblem(string detail) =>
        ProblemDetailsHelper.Validation(
            detail,
            HttpContext.Request.Path.ToString(),
            HttpContext.TraceIdentifier);

    private string GetUserKey() =>
        User.FindFirst("sub")?.Value ??
        User.Identity?.Name ??
        HttpContext.Connection.RemoteIpAddress?.ToString() ??
        "unknown";

    private static AuditSearchQuery ToQuery(AuditSearchRequest request) => new()
    {
        TenantId = request.TenantId,
        Method = request.Method,
        Endpoint = request.Endpoint,
        StatusCode = request.StatusCode,
        FromDate = request.FromDate,
        ToDate = request.ToDate,
        CorrelationId = request.CorrelationId,
        PageNumber = request.PageNumber,
        PageSize = request.PageSize
    };

    private static AuditExportQuery ToQuery(AuditExportRequest request) => new()
    {
        TenantId = request.TenantId,
        Method = request.Method,
        Endpoint = request.Endpoint,
        StatusCode = request.StatusCode,
        FromDate = request.FromDate,
        ToDate = request.ToDate,
        CorrelationId = request.CorrelationId,
        Format = request.Format
    };

    private static AuditLogItem ToDto(AuditLogResultItem item) => new()
    {
        Id = item.Id,
        TenantId = item.TenantId,
        Method = item.Method,
        Endpoint = item.Endpoint,
        StatusCode = item.StatusCode,
        DurationMs = item.DurationMs,
        Timestamp = item.Timestamp,
        CorrelationId = item.CorrelationId,
        ErrorMessage = item.ErrorMessage
    };

    private static string NormalizeFormat(string? format) =>
        string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant();

    private static string NormalizeKeyPart(string value) =>
        Uri.EscapeDataString(value.Trim().ToLowerInvariant());
}
