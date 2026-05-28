using ERPApiHub.API.DTOs.Audit;
using ERPApiHub.Application.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers;

[ApiController]
[Route("api/v2/audit")]
[Authorize]
public sealed class AuditController(AuditSearchService service) : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "success",
        "failure",
        "warning"
    };

    private static readonly HashSet<string> AllowedExportFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "csv",
        "json"
    };

    [HttpGet("search")]
    [Authorize(Policy = "api-hub:admin")]
    [ProducesResponseType(typeof(AuditSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditSearchResponse>> Search(
        [FromQuery] AuditSearchRequest request,
        CancellationToken ct)
    {
        var validationError = ValidateSearchRequest(request);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var result = await service.SearchAsync(new AuditSearchQuery(
            request.TenantId,
            request.SystemId,
            request.EventType,
            request.FromDate,
            request.ToDate,
            request.Status,
            request.UserId,
            request.Endpoint,
            request.CorrelationId,
            request.Page,
            request.PageSize,
            request.SortBy ?? "createdAt",
            request.SortDirection ?? "desc"), ct);

        return Ok(MapResponse(result));
    }

    [HttpGet("export")]
    [Authorize(Policy = "api-hub:admin")]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromQuery] AuditExportRequest request,
        CancellationToken ct)
    {
        var validationError = ValidateExportRequest(request);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var format = request.Format.Trim().ToLowerInvariant();
        var stream = await service.ExportAsync(new AuditExportQuery(
            request.TenantId,
            request.SystemId,
            request.EventType,
            request.FromDate,
            request.ToDate,
            request.Status,
            format,
            request.CorrelationId,
            request.MaxRecords), ct);

        var contentType = format == "json" ? "application/json" : "text/csv";
        var extension = format == "json" ? "json" : "csv";
        var fileName = $"audit-export-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}";

        return File(stream, contentType, fileName);
    }

    [HttpGet("stats")]
    [Authorize(Policy = "api-hub:admin")]
    [ProducesResponseType(typeof(AuditStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditStatsResponse>> GetStats(
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        CancellationToken ct)
    {
        if (fromDate is not null && toDate is not null && fromDate > toDate)
        {
            return BadRequest(new { error = "fromDate must be before or equal to toDate." });
        }

        var stats = await service.GetStatsAsync(fromDate, toDate, ct);
        return Ok(new AuditStatsResponse
        {
            TotalEvents = stats.TotalEvents,
            SuccessCount = stats.SuccessCount,
            FailureCount = stats.FailureCount,
            WarningCount = stats.WarningCount,
            EventsByType = stats.EventsByType,
            EventsByTenant = stats.EventsByTenant,
            EventsByDay = stats.EventsByDay
        });
    }

    private static string? ValidateSearchRequest(AuditSearchRequest request)
    {
        if (request.FromDate is not null && request.ToDate is not null && request.FromDate > request.ToDate)
        {
            return "fromDate must be before or equal to toDate.";
        }

        if (request.Page < 1)
        {
            return "page must be greater than or equal to 1.";
        }

        if (request.PageSize < 1)
        {
            return "pageSize must be greater than or equal to 1.";
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && !AllowedStatuses.Contains(request.Status))
        {
            return "status must be one of: success, failure, warning.";
        }

        if (!string.IsNullOrWhiteSpace(request.SortDirection) &&
            !request.SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) &&
            !request.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase))
        {
            return "sortDirection must be either asc or desc.";
        }

        return null;
    }

    private static string? ValidateExportRequest(AuditExportRequest request)
    {
        if (request.FromDate is not null && request.ToDate is not null && request.FromDate > request.ToDate)
        {
            return "fromDate must be before or equal to toDate.";
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && !AllowedStatuses.Contains(request.Status))
        {
            return "status must be one of: success, failure, warning.";
        }

        if (string.IsNullOrWhiteSpace(request.Format) || !AllowedExportFormats.Contains(request.Format))
        {
            return "format must be either csv or json.";
        }

        if (request.MaxRecords is < 1)
        {
            return "maxRecords must be greater than or equal to 1.";
        }

        return null;
    }

    private static AuditSearchResponse MapResponse(AuditSearchResult result) =>
        new()
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages,
            Items = result.Items.Select(MapLog).ToList()
        };

    private static AuditLogDto MapLog(AuditLogResult item) =>
        new()
        {
            Id = item.Id,
            TenantId = item.TenantId,
            SystemId = item.SystemId,
            EventType = item.EventType,
            Description = item.Description,
            Status = item.Status,
            UserId = item.UserId,
            Endpoint = item.Endpoint,
            StatusCode = item.StatusCode,
            DurationMs = item.DurationMs,
            CorrelationId = item.CorrelationId,
            IpAddress = item.IpAddress,
            CreatedAt = item.CreatedAt
        };
}
