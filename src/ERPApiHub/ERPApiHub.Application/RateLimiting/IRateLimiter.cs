namespace ERPApiHub.Application.RateLimiting;

public interface IRateLimiter
{
    Task<RateLimitResult> CheckAsync(
        string systemId,
        RateLimitTier tier,
        EndpointType endpointType,
        CancellationToken ct);

    Task<bool> CheckBurstAsync(
        string systemId,
        RateLimitTier tier,
        CancellationToken ct);

    RateLimitTier ResolveTier(string? rateLimitTierStr);
}
