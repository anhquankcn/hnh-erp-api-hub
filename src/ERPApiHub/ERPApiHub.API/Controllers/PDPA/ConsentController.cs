using ERPApiHub.API.DTOs.PDPA;
using ERPApiHub.Application.Compliance;
using ERPApiHub.Application.Errors;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers.PDPA;

/// <summary>
/// PDPA consent endpoints with durable database persistence.
/// </summary>
[ApiController]
[Route("api/v2")]
[Authorize]
public sealed class ConsentController(PdpaService pdpaService) : ControllerBase
{
    /// <summary>
    /// Submit consent for a data subject.
    /// </summary>
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
        var tenantId = GetTenantId();

        var consent = await pdpaService.GrantConsentAsync(
            tenantId,
            request.SubjectId,
            request.Purpose,
            request.Doctypes.ToList(),
            request.Notes,
            request.ExpiryDate,
            cancellationToken);

        var response = MapToResponse(consent);
        return Created($"/api/v2/consent/{consent.Id}", response);
    }

    /// <summary>
    /// Withdraw consent by purpose.
    /// </summary>
    [HttpPost("consent/{purpose}/withdraw")]
    [ProducesResponseType(typeof(ConsentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    [Authorize(Policy = "api-hub:write")]
    public async Task<ActionResult<ConsentResponse>> WithdrawConsent(
        [FromRoute] string purpose,
        [FromBody] WithdrawConsentRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var consent = await pdpaService.WithdrawConsentAsync(
            tenantId,
            request.SubjectId,
            purpose,
            request.Reason,
            cancellationToken);

        return Ok(MapToResponse(consent));
    }

    /// <summary>
    /// Get consent status for a data subject.
    /// </summary>
    [HttpGet("consent/{subjectId}/status")]
    [ProducesResponseType(typeof(ConsentStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [Authorize(Policy = "api-hub:read")]
    public async Task<ActionResult<ConsentStatusResponse>> GetConsentStatus(
        [FromRoute] string subjectId,
        [FromQuery] string? purpose = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();

        var consents = await pdpaService.GetConsentsBySubjectAsync(tenantId, subjectId, cancellationToken);
        var filteredConsents = string.IsNullOrWhiteSpace(purpose)
            ? consents
            : consents.Where(c => string.Equals(c.Purpose, purpose, StringComparison.OrdinalIgnoreCase)).ToList();

        return Ok(new ConsentStatusResponse
        {
            SubjectId = subjectId,
            HasActiveConsent = filteredConsents.Any(c => c.IsActive),
            Consents = filteredConsents.Select(MapToResponse).ToList()
        });
    }

    /// <summary>
    /// Get detailed consent record.
    /// </summary>
    [HttpGet("consent/{subjectId}/{purpose}")]
    [ProducesResponseType(typeof(ConsentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    [Authorize(Policy = "api-hub:read")]
    public async Task<ActionResult<ConsentResponse>> GetConsent(
        [FromRoute] string subjectId,
        [FromRoute] string purpose,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var consent = await pdpaService.GetConsentAsync(tenantId, subjectId, purpose, cancellationToken);

        if (consent is null)
        {
            return NotFound(new ErpHubProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Consent not found",
                Detail = $"No consent found for subject {subjectId} and purpose {purpose}"
            });
        }

        return Ok(MapToResponse(consent));
    }

    private static ConsentResponse MapToResponse(ConsentRecord consent)
    {
        return new ConsentResponse
        {
            Id = consent.Id.ToString("N"),
            SubjectId = consent.DataSubjectId,
            Purpose = consent.Purpose,
            Doctypes = consent.Doctypes,
            Status = consent.IsActive ? "granted" : "withdrawn",
            GrantedAt = consent.GrantedAt,
            ExpiryDate = consent.ExpiresAt,
            WithdrawnAt = consent.WithdrawnAt,
            Notes = consent.Notes
        };
    }

    private string GetTenantId()
    {
        return User.FindFirst("BranchId")?.Value
            ?? User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("Tenant identifier not found in claims.");
    }
}
