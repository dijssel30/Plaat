using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Plaat.Db.Repositories;

/// <summary>
/// In-process cache for the expensive whole-library projections. Replaces <c>HybridCache</c>.
/// <para>
/// Two things make it worth hand-rolling instead of using a stock cache:
/// </para>
/// <list type="bullet">
/// <item>
/// Its own per-key <see cref="SemaphoreSlim"/> single-flight, so a cold rebuild of a big list
/// (all tracks/albums/artists) runs exactly once no matter how many requests arrive during it.
/// </item>
/// <item>
/// <see cref="Mutate{T}"/> / <see cref="Update{T}"/>: callers keep cached entries correct by
/// editing them in place (a play-count bump, a track added by a scan) instead of evicting and
/// forcing the next visitor to pay a multi-second cold recompute.
/// </item>
/// </list>
/// Entries never expire on a timer - invalidation is entirely the caller's responsibility.
/// </summary>
public interface ILibraryCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or runs <paramref name="factory"/>
    /// exactly once (across all concurrent callers) to produce and cache it.
    /// </summary>
    ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken ct);

    bool TryGet<T>(string key, out T value);

    /// <summary>
    /// Unconditionally (re)populates <paramref name="key"/> with a freshly computed value.
    /// </summary>
    void Set<T>(string key, T value);

    void Remove(string key);

    /// <summary>
    /// Removes every entry whose key starts with <paramref name="prefix"/>.
    /// </summary>
    void RemoveByPrefix(string prefix);

    /// <summary>
    /// Runs <paramref name="mutate"/> against the cached value under its key-lock, only if the
    /// entry is present. No-op on a miss. Use for in-place edits to a reference-typed entry
    /// (e.g. bumping <see cref="Plaat.Contract.TrackDto.PlayCount"/> on a cached list element).
    /// </summary>
    void Mutate<T>(string key, Action<T> mutate);

    /// <summary>
    /// Replaces the cached value with <c>update(current)</c> under its key-lock, only if the
    /// entry is present. Use when the entry (or the shape being changed) is immutable and has to
    /// be rebuilt rather than edited (e.g. <see cref="Plaat.Contract.Models.StatsDto"/>).
    /// </summary>
    void Update<T>(string key, Func<T, T> update);
}

internal sealed class LibraryCache(IMemoryCache cache) : ILibraryCache
{
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

    /// <summary>
    /// Number of key-locks currently held or waited on. Gates are ref-counted and dropped as soon
    /// as nobody uses them, so this is zero whenever the cache is idle.
    /// </summary>
    internal int ActiveGateCount
    {
        get
        {
            lock (_gates)
            {
                return _gates.Count;
            }
        }
    }

    private sealed class Gate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Refs;
    }

    /// <summary>
    /// A held key-lock. Disposing releases the semaphore and drops the gate once its last user is done.
    /// </summary>
    private readonly struct Lease(LibraryCache owner, string key, Gate gate) : IDisposable
    {
        public void Dispose()
        {
            gate.Semaphore.Release();
            owner.ReleaseRef(key, gate);
        }
    }

    private Gate AcquireRef(string key)
    {
        lock (_gates)
        {
            if (!_gates.TryGetValue(key, out var gate))
            {
                gate = new Gate();
                _gates[key] = gate;
            }

            gate.Refs++;

            return gate;
        }
    }

    private void ReleaseRef(string key, Gate gate)
    {
        lock (_gates)
        {
            // Only removed once no one holds or waits on it, so a live gate is never replaced
            // by a second one for the same key (which would break single-flight).
            if (--gate.Refs == 0)
            {
                _gates.Remove(key);
            }
        }
    }

    private async ValueTask<Lease> LockAsync(string key, CancellationToken ct)
    {
        var gate = AcquireRef(key);

        try
        {
            await gate.Semaphore.WaitAsync(ct);
        }
        catch
        {
            ReleaseRef(key, gate);

            throw;
        }

        return new Lease(this, key, gate);
    }

    private Lease Lock(string key)
    {
        var gate = AcquireRef(key);
        gate.Semaphore.Wait();

        return new Lease(this, key, gate);
    }

    public async ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken ct)
    {
        if (cache.TryGetValue(key, out T? existing))
        {
            return existing!;
        }

        using var lease = await LockAsync(key, ct);

        if (cache.TryGetValue(key, out existing))
        {
            return existing!;
        }

        var value = await factory(ct);
        StoreLocked(key, value);

        return value;
    }

    public bool TryGet<T>(string key, out T value)
    {
        if (cache.TryGetValue(key, out T? found) && found is not null)
        {
            value = found;

            return true;
        }

        value = default!;

        return false;
    }

    public void Set<T>(string key, T value)
    {
        using var lease = Lock(key);

        StoreLocked(key, value);
    }

    public void Remove(string key)
    {
        cache.Remove(key);
        _keys.TryRemove(key, out _);
    }

    public void RemoveByPrefix(string prefix)
    {
        foreach (var key in MatchingKeys(prefix))
        {
            Remove(key);
        }
    }

    public void Mutate<T>(string key, Action<T> mutate)
    {
        using var lease = Lock(key);

        if (cache.TryGetValue(key, out T? value) && value is not null)
        {
            mutate(value);
        }
    }

    public void Update<T>(string key, Func<T, T> update)
    {
        using var lease = Lock(key);

        if (cache.TryGetValue(key, out T? value) && value is not null)
        {
            StoreLocked(key, update(value));
        }
    }

    private void StoreLocked<T>(string key, T value)
    {
        _keys[key] = 0;

        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            // We own invalidation; nothing should drop an entry behind our back.
            Priority = CacheItemPriority.NeverRemove,
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = static (evicted, _, _, state) =>
                        ((ConcurrentDictionary<string, byte>)state!).TryRemove((string)evicted, out _),
                    State = _keys
                }
            }
        });
    }

    private string[] MatchingKeys(string prefix)
    {
        return [.. _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))];
    }
}
