using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BaGetter.Core.Tests.Upstream;

public class UpstreamMetadataCacheTests
{
    [Fact]
    public async Task CoalescesConcurrentMetadataRequests()
    {
        using var cache = new UpstreamMetadataCache();
        var originCalls = 0;
        async Task<string> Fetch()
        {
            Interlocked.Increment(ref originCalls);
            await Task.Delay(20);
            return "versions";
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            cache.GetOrAddAsync("nuget:example", TimeSpan.FromMinutes(1), Fetch, CancellationToken.None)));
        Assert.All(results, x => Assert.Equal("versions", x));
        Assert.Equal(1, originCalls);
    }

    [Fact]
    public async Task DoesNotCacheOriginFailures()
    {
        using var cache = new UpstreamMetadataCache();
        var calls = 0;
        async Task<string> Fetch()
        {
            Interlocked.Increment(ref calls);
            await Task.Yield();
            throw new IOException("origin down");
        }
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<IOException>(() =>
                cache.GetOrAddAsync("failed", TimeSpan.FromMinutes(1), Fetch, CancellationToken.None));
        }
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ZeroDurationDisablesCaching()
    {
        using var cache = new UpstreamMetadataCache();
        var count = 0;
        Task<string> Fetch() => Task.FromResult(Interlocked.Increment(ref count).ToString());
        Assert.Equal("1", await cache.GetOrAddAsync("key", TimeSpan.Zero, Fetch, CancellationToken.None));
        Assert.Equal("2", await cache.GetOrAddAsync("key", TimeSpan.Zero, Fetch, CancellationToken.None));
    }
}
