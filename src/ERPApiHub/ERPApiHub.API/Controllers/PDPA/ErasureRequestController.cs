using ERPApiHub.API.DTOs.PDPA;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Compliance;
using ERPApiHub.Application.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers.PDPA;

/// <summary>
/// PDPA data erasure request endpoints.
/// </summary>
[ApiController]
[Route("api/v2")]
[Authorize]
public sealed class ErasureRequestController(
    ConsentService consentService,
    ICacheService cacheService) : ControllerBase
{
    private static readonly TimeSpan ErasureTtl = TimeSpan.FromDays(365);
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Submit a data erasure request for asynchronous processing.
    /// </summary>
    /// <param name="request">Erasure request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accepted erasure request.</returns>
    [HttpPost("erasure-request")]
    [ProducesResponseType(typeof(ErasureRequestResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status429TooManyRequests)]
    [Authorize(Policy = "api-hub:write")]
    public async Task<ActionResult<ErasureRequestResponse>> SubmitErasureRequest(
        [FromBody] CreateErasureRequest request,
        CancellationToken cancellationToken)
    {
        if (await IsRateLimitedAsync("erasure-request", request.SubjectId, 5, cancellationToken))
        {
            return RateLimited("Erasure request rate limit exceeded.");
        }

        var tenantId = GetTenantId();
        var requestedBy = User.FindFirst("sub")?.Value ?? User.Identity?.Name ?? "unknown";

        await consentService.RequestDataErasureAsync(
            tenantId,
            request.SubjectId,
            request.Reason,
            requestedBy,
            cancellationToken);

        var response = new ErasureRequestResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            SubjectId = request.SubjectId,
            Reason = request.Reason,
            RequestedDoctypes = request.RequestedDoctypes,
            Status = "requested",
            RequestedAt = DateTimeOffset.UtcNow
        };

        var status = new ErasureStatusResponse
        {
            Id = response.Id,
            SubjectId = response.SubjectId,
            Status = response.Status,
            RequestedDoctypes = response.RequestedDoctypes,
            RequestedAt = response.RequestedAt
        };

        await cacheService.SetAsync(ErasureKey(tenantId, response.Id), status, ErasureTtl, cancellationToken);

        return Accepted($"/api/v2/erasure-request/{response.Id}/status", response);
    }

    /// <summary>
    /// Check erasure request status.
    /// </summary>
    /// <param name="id">Erasure request identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The erasure request status.</returns>
    [HttpGet("erasure-request/{id}/status")]
    [Authorize(Policy = "api-hub:read")]
    [ProducesResponseType(typeof(ErasureStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ErasureStatusResponse>> GetErasureStatus(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var status = await cacheService.GetAsync<ErasureStatusResponse>(
            ErasureKey(tenantId, id),
            cancellationToken);

        if (status is null)
        {
            return NotFound(ProblemDetailsHelper.NotFound(
                $"Erasure request {id} was not found.",
                HttpContext.Request.Path.ToString(),
                HttpContext.TraceIdentifier));
        }

        return Ok(status);
    }

    private async Task<bool> IsRateLimitedAsync(
        string endpoint,
        string subjectId,
        int limit,
        CancellationToken cancellationToken)
    {
        var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"pdpa:ratelimit:{endpoint}:{NormalizeKeyPart(subjectId)}:{window}";
        var count = await cacheService.IncrementAsync(key, cancellationToken);

        if (count == 1)
        {
            await cacheService.ExpireAsync(key, RateLimitWindow, cancellationToken);
        }

        return count > limit;
    }

    private ActionResult<ErasureRequestResponse> RateLimited(string detail)
    {
        Response.Headers["Retry-After"] = ((int)RateLimitWindow.TotalSeconds).ToString();
        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            ProblemDetailsHelper.RateLimited(
                detail,
                (int)RateLimitWindow.TotalSeconds,
                HttpContext.Request.Path.ToString(),
                HttpContext.TraceIdentifier));
    }

    private string GetTenantId() => User.FindFirst("BranchId")?.Value ?? "unknown";

    private static string ErasureKey(string tenantId, string id) =>
        $"pdpa:erasure-request:{NormalizeKeyPart(tenantId)}:{NormalizeKeyPart(id)}";

    private static string NormalizeKeyPart(string value) =>
        Uri.EscapeDataString(value.Trim().ToLowerInvariant());
}
