using ERPApiHub.API.DTOs.PDPA;
using ERPApiHub.Application.Compliance;
using ERPApiHub.Application.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers.PDPA;

/// <summary>
/// PDPA erasure request endpoints.
/// </summary>
[ApiController]
[Route("api/v2")]
[Authorize]
public sealed class ErasureRequestController(PdpaService pdpaService) : ControllerBase
{
    /// <summary>
    /// Submit an erasure request.
    /// </summary>
    [HttpPost("erasure")]
    [ProducesResponseType(typeof(ErasureRequestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [Authorize(Policy = "api-hub:write")]
    public async Task<ActionResult<ErasureRequestResponse>> SubmitErasureRequest(
        [FromBody] CreateErasureRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var erasureRequest = await pdpaService.RequestDataErasureAsync(
            tenantId,
            request.SubjectId,
            request.Reason ?? "User requested data erasure",
            cancellationToken);

        var response = MapToResponse(erasureRequest);
        return Created($"/api/v2/erasure/{erasureRequest.Id}", response);
    }

    /// <summary>
    /// Get erasure request status.
    /// </summary>
    [HttpGet("erasure/{id:guid}")]
    [ProducesResponseType(typeof(ErasureStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    [Authorize(Policy = "api-hub:read")]
    public async Task<ActionResult<ErasureStatusResponse>> GetErasureStatus(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var request = await pdpaService.GetErasureRequestAsync(id, cancellationToken);

        if (request is null)
        {
            return NotFound(new ErpHubProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Erasure request not found",
                Detail = $"No erasure request found with ID {id}"
            });
        }

        return Ok(new ErasureStatusResponse
        {
            Id = request.Id.ToString("N"),
            SubjectId = request.DataSubjectId,
            Status = request.Status,
            RequestedAt = request.RequestedAt,
            CompletedAt = request.CompletedAt,
            Notes = request.Notes
        });
    }

    /// <summary>
    /// List erasure requests for a subject.
    /// </summary>
    [HttpGet("erasure/subject/{subjectId}")]
    [ProducesResponseType(typeof(IEnumerable<ErasureStatusResponse>), StatusCodes.Status200OK)]
    [Authorize(Policy = "api-hub:read")]
    public async Task<ActionResult<IEnumerable<ErasureStatusResponse>>> GetSubjectErasureRequests(
        [FromRoute] string subjectId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var requests = await pdpaService.GetErasureRequestsBySubjectAsync(tenantId, subjectId, cancellationToken);

        return Ok(requests.Select(MapToResponse));
    }

    private static ErasureRequestResponse MapToResponse(ErasureRequest request)
    {
        return new ErasureRequestResponse
        {
            Id = request.Id.ToString("N"),
            SubjectId = request.DataSubjectId,
            Status = request.Status,
            RequestedAt = request.RequestedAt,
            CompletedAt = request.CompletedAt,
            Notes = request.Notes
        };
    }

    private static ErasureStatusResponse MapToResponse(ErasureRequest request)
    {
        return new ErasureStatusResponse
        {
            Id = request.Id.ToString("N"),
            SubjectId = request.DataSubjectId,
            Status = request.Status,
            RequestedAt = request.RequestedAt,
            CompletedAt = request.CompletedAt,
            Notes = request.Notes
        };
    }

    private string GetTenantId()
    {
        return User.FindFirst("BranchId")?.Value
            ?? User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("Tenant identifier not found in claims.");
    }
}
