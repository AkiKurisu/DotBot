using System.Collections.Concurrent;

namespace DotBot.Gateway;

/// <summary>
/// Provides per-session mutual exclusion so that concurrent requests targeting the same
/// sessionId are serialized, while different sessions remain fully parallel.
/// </summary>
public sealed class SessionGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>
    /// Acquires exclusive access for the given session. Dispose the returned handle to release.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken ct = default)
    {
        var semaphore = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
