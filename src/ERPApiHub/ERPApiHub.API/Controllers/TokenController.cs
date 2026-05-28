using ERPApiHub.API.DTOs.Tokens;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Auth;
using ERPApiHub.Application.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers;

/// <summary>
/// API token lifecycle management endpoints.
/// </summary>
[ApiController]
[Route("api/v2/tokens")]
[Authorize]
public sealed class TokenController(
    TokenService tokenService,
    ICacheService cacheService) : ControllerBase
{
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Create a new API token for an external system.
    /// </summary>
    /// <param name="request">Token creation payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created token, including the plain token value once.</returns>
    [HttpPost]
    [Authorize(Policy = "api-hub:admin")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TokenResponse>> CreateToken(
        [FromBody] CreateTokenRequest request,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateCreateRequest(request);
        if (validationProblem is not null)
            return BadRequest(validationProblem);

        if (await IsRateLimitedAsync("create", request.SystemId, 5, cancellationToken))
            return RateLimited<TokenResponse>("Token creation rate limit exceeded.");

        var token = await tokenService.GenerateTokenAsync(
            new CreateApiTokenCommand(
                request.SystemId,
                request.Description,
                request.ExpiryDays,
                NormalizePermissions(request.Permissions)),
            GetActorId(),
            cancellationToken);

        var response = MapToken(token, includePlainToken: true);
        return Created($"/api/v2/tokens/{response.Id}", response);
    }

    /// <summary>
    /// Rotate an API token and return the new plain token value once.
    /// </summary>
    /// <param name="id">Token identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rotated token, including the new plain token value.</returns>
    [HttpPost("{id}/rotate")]
    [Authorize(Policy = "api-hub:admin")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TokenResponse>> RotateToken(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        if (await IsRateLimitedAsync("rotate", id, 10, cancellationToken))
            return RateLimited<TokenResponse>("Token rotation rate limit exceeded.");

        var token = await tokenService.RotateTokenAsync(id, GetActorId(), cancellationToken);
        if (token is null)
            return TokenNotFound(id);

        return Ok(MapToken(token, includePlainToken: true));
    }

    /// <summary>
    /// Revoke an API token.
    /// </summary>
    /// <param name="id">Token identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content when the token is revoked.</returns>
    [HttpPost("{id}/revoke")]
    [Authorize(Policy = "api-hub:admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RevokeToken(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        if (await IsRateLimitedAsync("revoke", id, 10, cancellationToken))
            return RateLimited("Token revocation rate limit exceeded.");

        var revoked = await tokenService.RevokeTokenAsync(id, GetActorId(), cancellationToken);
        if (!revoked)
            return TokenNotFound(id);

        return NoContent();
    }

    /// <summary>
    /// List API tokens with optional system and status filters.
    /// </summary>
    /// <param name="systemId">Optional external system identifier filter.</param>
    /// <param name="status">Optional token status filter.</param>
    /// <param name="page">Page number.</param>
    /// <param name="pageSize">Number of tokens per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of token metadata.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(TokenListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenListResponse>> ListTokens(
        [FromQuery] string? systemId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page <= 0 || pageSize <= 0 || pageSize > 100)
        {
            return BadRequest(ProblemDetailsHelper.Validation(
                "Page must be greater than 0 and PageSize must be between 1 and 100.",
                HttpContext.Request.Path.ToString(),
                HttpContext.TraceIdentifier));
        }

        var result = await tokenService.ListTokensAsync(systemId, status, page, pageSize, cancellationToken);
        return Ok(new TokenListResponse
        {
            Items = result.Items.Select(t => MapToken(t, includePlainToken: false)).ToList(),
            Total = result.Total,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = (int)Math.Ceiling((double)result.Total / result.PageSize)
        });
    }

    /// <summary>
    /// Get API token details.
    /// </summary>
    /// <param name="id">Token identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token metadata.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TokenResponse>> GetToken(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetTokenAsync(id, cancellationToken);
        if (token is null)
            return TokenNotFound(id);

        return Ok(MapToken(token, includePlainToken: false));
    }

    /// <summary>
    /// Validate an API token.
    /// </summary>
    /// <param name="request">Token validation payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token validation result.</returns>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(TokenValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErpHubProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TokenValidationResponse>> ValidateToken(
        [FromBody] ValidateTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(ProblemDetailsHelper.Validation(
                "Token is required.",
                HttpContext.Request.Path.ToString(),
                HttpContext.TraceIdentifier));
        }

        if (await IsRateLimitedAsync("validate", GetClientKey(), 100, cancellationToken))
            return RateLimited<TokenValidationResponse>("Token validation rate limit exceeded.");

        var result = await tokenService.ValidateTokenAsync(request.Token, cancellationToken);
        return Ok(new TokenValidationResponse
        {
            IsValid = result.IsValid,
            TokenId = result.TokenId,
            SystemId = result.SystemId,
            Permissions = result.Permissions,
            ExpiresAt = result.ExpiresAt,
            Reason = result.Reason
        });
    }

    private async Task<bool> IsRateLimitedAsync(
        string endpoint,
        string subject,
        int limit,
        CancellationToken cancellationToken)
    {
        var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"tokens:ratelimit:{endpoint}:{NormalizeKeyPart(subject)}:{window}";
        var count = await cacheService.IncrementAsync(key, cancellationToken);

        if (count == 1)
            await cacheService.ExpireAsync(key, RateLimitWindow, cancellationToken);

        return count > limit;
    }

    private ActionResult<T> RateLimited<T>(string detail)
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

    private ObjectResult RateLimited(string detail)
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

    private NotFoundObjectResult TokenNotFound(string id) =>
        NotFound(ProblemDetailsHelper.NotFound(
            $"Token {id} was not found.",
            HttpContext.Request.Path.ToString(),
            HttpContext.TraceIdentifier));

    private ErpHubProblemDetails? ValidateCreateRequest(CreateTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SystemId))
            return ValidationProblem("SystemId is required.");

        if (request.ExpiryDays <= 0)
            return ValidationProblem("ExpiryDays must be greater than 0.");

        return null;
    }

    private ErpHubProblemDetails ValidationProblem(string detail) =>
        ProblemDetailsHelper.Validation(
            detail,
            HttpContext.Request.Path.ToString(),
            HttpContext.TraceIdentifier);

    private string GetActorId() =>
        User.FindFirst("sub")?.Value ?? User.Identity?.Name ?? "unknown";

    private string GetClientKey() =>
        User.FindFirst("sub")?.Value ??
        HttpContext.Connection.RemoteIpAddress?.ToString() ??
        "unknown";

    private static TokenResponse MapToken(ApiTokenRecord token, bool includePlainToken) => new()
    {
        Id = token.Id,
        SystemId = token.SystemId,
        Description = token.Description,
        Permissions = token.Permissions,
        Status = token.Status,
        Token = includePlainToken ? token.PlainToken : null,
        CreatedAt = token.CreatedAt,
        ExpiresAt = token.ExpiresAt,
        RotatedAt = token.RotatedAt,
        RevokedAt = token.RevokedAt
    };

    private static IReadOnlyList<string> NormalizePermissions(IEnumerable<string>? permissions) =>
        permissions?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static string NormalizeKeyPart(string value) =>
        Uri.EscapeDataString(value.Trim().ToLowerInvariant());
}
