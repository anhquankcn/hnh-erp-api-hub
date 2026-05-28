using ERPApiHub.API.DTOs.PDPA;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Compliance;
using ERPApiHub.Application.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers.PDPA;

/// <summary>
/// PDPA consent endpoints.
/// </summary>
[ApiController]
[Route("api/v2")]
[Authorize]
public sealed class ConsentController(
    ConsentService consentService,
    ICacheService cacheService) : ControllerBase
{
    private static readonly TimeSpan ConsentTtl = TimeSpan.FromDays(365);
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Submit consent for a data subject.
    /// </summary>
    /// <param name="request">Consent request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created consent record.</returns>
    [HttpPost("consent")]
    [ProducesResponseType(typeof(ConsentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status429TooManyRequests)]
    [Authorize(Policy = "api-hub:write")]
    public async Task<ActionResult<ConsentResponse>> SubmitConsent(
        [FromBody] CreateConsentRequest request,
        CancellationToken cancellationToken)
    {
        if (await IsRateLimitedAsync("consent", request.SubjectId, 10, cancellationToken))
        {
            return RateLimited("Consent submission rate limit exceeded.");
        }

        var tenantId = GetTenantId();
        var response = new ConsentResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            SubjectId = request.SubjectId,
            Purpose = request.Purpose,
            Doctypes = request.Doctypes,
            Status = "granted",
            GrantedAt = DateTimeOffset.UtcNow,
            ExpiryDate = request.ExpiryDate,
            Notes = request.Notes
        };

        await cacheService.SetAsync(ConsentKey(tenantId, response.Id), response, ConsentTtl, cancellationToken);
        await cacheService.SetAsync(SubjectConsentKey(tenantId, response.SubjectId), response, ConsentTtl, cancellationToken);

        return Created($"/api/v2/consent/{response.Id}", response);
    }

    /// <summary>
    /// Withdraw consent by consent identifier.
    /// </summary>
    /// <param name="id">Consent identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The withdrawn consent record.</returns>
    [HttpPost("consent/{id}/withdraw")]
    [ProducesResponseType(typeof(ConsentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    [Authorize(Policy = "api-hub:write")]
    public async Task<ActionResult<ConsentResponse>> WithdrawConsent(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var existing = await cacheService.GetAsync<ConsentResponse>(ConsentKey(tenantId, id), cancellationToken);
        if (existing is null)
        {
            return NotFound(ProblemDetailsHelper.NotFound(
                $"Consent {id} was not found.",
                HttpContext.Request.Path.ToString(),
                HttpContext.TraceIdentifier));
        }

        await consentService.WithdrawConsentAsync(
            tenantId,
            existing.Purpose,
            existing.SubjectId,
            cancellationToken: cancellationToken);

        var response = existing with
        {
            Status = "withdrawn",
            WithdrawnAt = DateTimeOffset.UtcNow
        };

        await cacheService.SetAsync(ConsentKey(tenantId, id), response, ConsentTtl, cancellationToken);
        await cacheService.SetAsync(SubjectConsentKey(tenantId, response.SubjectId), response, ConsentTtl, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Check consent status for a data subject.
    /// </summary>
    /// <param name="subjectId">Data subject identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The data subject consent status.</returns>
    [HttpGet("consent/status/{subjectId}")]
    [Authorize(Policy = "api-hub:read")]
    [ProducesResponseType(typeof(ConsentStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ConsentStatusResponse>> GetConsentStatus(
        [FromRoute] string subjectId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var consent = await cacheService.GetAsync<ConsentResponse>(
            SubjectConsentKey(tenantId, subjectId),
            cancellationToken);

        IReadOnlyList<ConsentResponse> consents = consent is null
            ? Array.Empty<ConsentResponse>()
            : [consent];
        var now = DateTimeOffset.UtcNow;

        return Ok(new ConsentStatusResponse
        {
            SubjectId = subjectId,
            HasActiveConsent = consents.Any(c =>
                c.Status.Equals("granted", StringComparison.OrdinalIgnoreCase) &&
                (c.ExpiryDate is null || c.ExpiryDate > now)),
            Consents = consents
        });
    }

    private async Task<bool> IsRateLimitedAsync(
        string endpoint,
        string subjectId,
        int limit,
        CancellationToken cancellationToken)
    {
        var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var count = await cacheService.IncrementAsync(
            $"pdpa:ratelimit:{endpoint}:{NormalizeKeyPart(subjectId)}:{window}",
            cancellationToken);

        if (count == 1)
        {
            await cacheService.ExpireAsync(
                $"pdpa:ratelimit:{endpoint}:{NormalizeKeyPart(subjectId)}:{window}",
                RateLimitWindow,
                cancellationToken);
        }

        return count > limit;
    }

    private ActionResult<ConsentResponse> RateLimited(string detail)
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

    private static string ConsentKey(string tenantId, string id) =>
        $"pdpa:consent:{NormalizeKeyPart(tenantId)}:{NormalizeKeyPart(id)}";

    private static string SubjectConsentKey(string tenantId, string subjectId) =>
        $"pdpa:consent-subject:{NormalizeKeyPart(tenantId)}:{NormalizeKeyPart(subjectId)}";

    private static string NormalizeKeyPart(string value) =>
        Uri.EscapeDataString(value.Trim().ToLowerInvariant());
}
