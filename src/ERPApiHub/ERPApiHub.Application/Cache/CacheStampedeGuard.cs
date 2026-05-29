using System.Collections.Concurrent;

namespace ERPApiHub.Application.Cache;

public sealed class CacheStampedeGuard
{
    private static readonly ConcurrentDictionary<string, KeyedSemaphore> KeyLocks = new();

    public async Task<T> ExecuteAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> readCache,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        var cached = await readCache(cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var keyLock = await RentKeyLockAsync(key);
        var lockAcquired = false;
        try
        {
            await keyLock.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;

            cached = await readCache(cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            return await factory(cancellationToken);
        }
        finally
        {
            if (lockAcquired)
            {
                keyLock.Semaphore.Release();
            }

            if (keyLock.ReleaseWaiterAndTryRetire())
            {
                KeyLocks.TryRemove(new KeyValuePair<string, KeyedSemaphore>(key, keyLock));
            }
        }
    }

    private static async Task<KeyedSemaphore> RentKeyLockAsync(string key)
    {
        while (true)
        {
            var keyLock = KeyLocks.GetOrAdd(key, _ => new KeyedSemaphore());
            if (keyLock.TryAddWaiter())
            {
                return keyLock;
            }

            await Task.Yield();
        }
    }

    private sealed class KeyedSemaphore
    {
        private const int Retired = -1;
        private int _waiterCount;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddWaiter()
        {
            while (true)
            {
                var current = Volatile.Read(ref _waiterCount);
                if (current == Retired)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _waiterCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public bool ReleaseWaiterAndTryRetire()
        {
            var remaining = Interlocked.Decrement(ref _waiterCount);
            return remaining == 0
                && Interlocked.CompareExchange(ref _waiterCount, Retired, 0) == 0;
        }
    }
}
