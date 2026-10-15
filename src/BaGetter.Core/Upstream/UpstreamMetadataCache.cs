using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGetter.Core;

/// <summary>
/// Bounded best-effort metadata cache with per-key request coalescing.
/// Only successful load results are cached, never upstream exceptions.
/// </summary>
public sealed class UpstreamMetadataCache : IDisposable
{
    private const int MaxEntries = 4096;
    private readonly ConcurrentDictionary<string, (object Value, DateTimeOffset Expires)> _cache =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<T> GetOrAddAsync<T>(
        string key,
        TimeSpan duration,
        Func<Task<T>> fetch,
        CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(fetch);

        if (duration <= TimeSpan.Zero) return await fetch();

        if (TryGet(key, out T hit)) return hit;

        var index = (StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % _gates.Length;
        var gate = _gates[index];
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGet(key, out hit)) return hit;

            var value = await fetch();
            if (value != null)
            {
                if (_cache.Count >= MaxEntries) _cache.Clear();
                _cache[key] = (value, DateTimeOffset.UtcNow.Add(duration));
            }
            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGet<T>(string key, out T value) where T : class
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
        {
            value = entry.Value as T;
            return value != null;
        }

        _cache.TryRemove(key, out _);
        value = null;
        return false;
    }

    public void Dispose()
    {
        foreach (var gate in _gates) gate.Dispose();
        _cache.Clear();
    }
}
