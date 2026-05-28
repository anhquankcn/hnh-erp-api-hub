namespace ERPApiHub.Application.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default);
    Task ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
