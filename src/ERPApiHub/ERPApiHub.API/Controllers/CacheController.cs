using ERPApiHub.API.Services.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers;

[ApiController]
[Route("api/v1/cache")]
[Authorize(Policy = "api-hub:admin")]
public sealed class CacheController(CacheInvalidationService invalidationService) : ControllerBase
{
    [HttpDelete("keys/{*key}")]
    public async Task<ActionResult<CacheInvalidationResult>> DeleteKey(
        string key,
        CancellationToken cancellationToken)
    {
        var result = await invalidationService.InvalidateKeyAsync(key, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("patterns")]
    public async Task<ActionResult<CacheInvalidationResult>> DeletePattern(
        [FromQuery] string pattern,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return BadRequest(new { error = "Query parameter 'pattern' is required." });
        }

        var result = await invalidationService.InvalidatePatternAsync(pattern, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("tags/{tag}")]
    public async Task<ActionResult<CacheInvalidationResult>> DeleteTag(
        string tag,
        CancellationToken cancellationToken)
    {
        var result = await invalidationService.InvalidateTagAsync(tag, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("doctypes/{doctype}")]
    public async Task<ActionResult<CacheInvalidationResult>> DeleteDoctype(
        string doctype,
        [FromQuery] string? tenantId,
        CancellationToken cancellationToken)
    {
        var result = await invalidationService.InvalidateDoctypeAsync(doctype, tenantId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("purge")]
    public async Task<ActionResult<IReadOnlyCollection<CacheInvalidationResult>>> Purge(
        CachePurgeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await invalidationService.PurgeAsync(request, cancellationToken);
        return Ok(result);
    }
}
