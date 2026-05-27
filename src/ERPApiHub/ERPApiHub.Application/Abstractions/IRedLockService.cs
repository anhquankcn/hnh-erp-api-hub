namespace ERPApiHub.Application.Abstractions;

public interface IRedLockService
{
    Task<bool> AcquireLockAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken);

    Task ReleaseLockAsync(string resource, CancellationToken cancellationToken);
}
