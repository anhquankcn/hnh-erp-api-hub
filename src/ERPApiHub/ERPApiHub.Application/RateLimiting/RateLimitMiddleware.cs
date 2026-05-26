using System.Text.Json;
using ERPApiHub.Application.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ERPApiHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ERPApiHub.Application.RateLimiting;

/// <summary>
/// Rate limiting middleware that checks request limits before endpoint execution.
/// FRD refs: FR-RLM-001~005.
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var rateLimitService = context.RequestServices.GetRequiredService<RateLimitService>();
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        if (!options.Enabled)
        {
            await _next(context);
            return;
        }

        // Skip rate limiting for non-API paths (health, metrics, internal)
        var path = context.Request.Path.Value ?? "/";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Skip for anonymous endpoints
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        // Resolve system_id from JWT claims or default
        var systemId = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst("preferred_username")?.Value
            ?? "anonymous";
        var tenantId = context.User.FindFirst("BranchId")?.Value;

        // Resolve tier — look up from external_systems if available, else default
        var tier = options.DefaultTier;
        var tierResolvedFromCache = false;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            try
            {
                var redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                var cached = await redis.GetDatabase().StringGetAsync(GetTierCacheKey(tenantId));

                if (cached.HasValue)
                {
                    var cachedSystem = JsonSerializer.Deserialize<CachedRateLimitSystem>(cached.ToString());
                    if (cachedSystem is not null)
                    {
                        tier = rateLimitService.ResolveTier(cachedSystem.RateLimitTier);
                        systemId = cachedSystem.SystemId;
                        tierResolvedFromCache = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis cache read failed for rate limit tier resolution. Falling through to DB.");
            }
        }

        if (!tierResolvedFromCache)
        {
            try
            {
                var dbContext = context.RequestServices.GetRequiredService<ErpHubDbContext>();
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    var system = await dbContext.ExternalSystems
                        .AsNoTracking()
                        .Where(s => s.TenantId == tenantId && s.IsActive && s.DeletedAt == null)
                        .FirstOrDefaultAsync(context.RequestAborted);

                    if (system is not null)
                    {
                        tier = rateLimitService.ResolveTier(system.RateLimitTier);
                        systemId = system.SystemId;

                        try
                        {
                            var redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                            var cachedSystem = new CachedRateLimitSystem(system.SystemId, system.RateLimitTier);
                            await redis.GetDatabase().StringSetAsync(
                                GetTierCacheKey(tenantId),
                                JsonSerializer.Serialize(cachedSystem),
                                TimeSpan.FromSeconds(60));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Redis cache write failed for rate limit tier resolution.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to look up external system for rate limiting. Using default tier.");
            }
        }

        // Determine endpoint type from path
        var endpointType = ClassifyEndpoint(path);

        // Check rate limit
        var result = await rateLimitService.CheckAsync(systemId, tier, endpointType, context.RequestAborted);

        // Set rate limit headers on all responses
        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = result.ResetInSeconds.ToString();

        if (!result.IsAllowed)
        {
            var problem = ProblemDetailsHelper.RateLimited(
                "Rate limit exceeded. Please retry later.",
                result.ResetInSeconds,
                path,
                context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = result.ResetInSeconds.ToString();
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
            return;
        }

        var burstResult = await rateLimitService.CheckBurstAsync(systemId, tier, context.RequestAborted);
        if (!burstResult)
        {
            var problem = ProblemDetailsHelper.RateLimited(
                "Burst limit exceeded. Please retry later.",
                result.ResetInSeconds,
                path,
                context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = result.ResetInSeconds.ToString();
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Classify request path into endpoint type for per-endpoint rate limiting.
    /// </summary>
    private static EndpointType ClassifyEndpoint(string path)
    {
        if (path.Contains("/ingest/", StringComparison.OrdinalIgnoreCase))
            return EndpointType.Ingestion;

        if (path.Contains("/query/", StringComparison.OrdinalIgnoreCase))
            return EndpointType.Query;

        if (path.Contains("/webhooks/", StringComparison.OrdinalIgnoreCase))
            return EndpointType.WebhookManagement;

        if (path.Contains("/audit/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/systems/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/cache/", StringComparison.OrdinalIgnoreCase))
            return EndpointType.Admin;

        return EndpointType.Other;
    }

    private static string GetTierCacheKey(string tenantId) => $"erphub:system-tier:{tenantId}";

    private sealed record CachedRateLimitSystem(string SystemId, string? RateLimitTier);
}
