using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Http;
using System.Threading;
using NuGet.Versioning;
using System.Threading.Tasks;
using BaGetter.Protocol.Internal;
using Xunit;

namespace BaGetter.Protocol.Tests;

public class RawPackageContentTests : IClassFixture<ProtocolFixture>
{
    private readonly RawPackageContentClient _target;

    public RawPackageContentTests(ProtocolFixture fixture)
    {
        _target = fixture.ContentClient;
    }

    [Fact]
    public async Task GetsPackageVersions()
    {
        var result = await _target.GetPackageVersionsOrNullAsync("Test.Package");

        Assert.NotNull(result);
        Assert.Equal(2, result.Versions.Count);
        Assert.Equal("1.0.0", result.Versions[0]);
        Assert.Equal("2.0.0", result.Versions[1]);
    }

    [Fact]
    public async Task ReturnsNullIfPackageDoesNotExist()
    {
        var result = await _target.GetPackageVersionsOrNullAsync(Guid.NewGuid().ToString());

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task PackageAndManifestDoNotAcceptUpstreamFailuresAsContent(HttpStatusCode status)
    {
        using var http = new HttpClient(new StubHandler(status));
        var client = new RawPackageContentClient(http, "https://example.test/v3/package");
        var version = NuGetVersion.Parse("1.2.3");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadPackageOrNullAsync("Test.Package", version));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadPackageManifestOrNullAsync("Test.Package", version));
    }

    [Fact]
    public async Task MissingPackageAndManifestReturnNull()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound));
        var client = new RawPackageContentClient(http, "https://example.test/v3/package");
        var version = NuGetVersion.Parse("1.2.3");

        Assert.Null(await client.DownloadPackageOrNullAsync("Test.Package", version));
        Assert.Null(await client.DownloadPackageManifestOrNullAsync("Test.Package", version));
    }

    [Fact]
    public async Task SuccessfulPackageDownloadReturnsOriginalBytes()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK));
        var client = new RawPackageContentClient(http, "https://example.test/v3/package");
        using var stream = await client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3"));
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retries429UsingRetryAfterAndReturnsOriginalBytes(bool manifest)
    {
        using var handler = new SequenceHandler(
            new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.OK }, TimeSpan.Zero);
        using var http = new HttpClient(handler);
        var client = new RawPackageContentClient(
            http, "https://example.test/v3/package", maxTransientRetries: 2);

        using var content = manifest
            ? await client.DownloadPackageManifestOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3"))
            : await client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3"));
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Retries503AtMostConfiguredNumberOfTimes()
    {
        using var handler = new SequenceHandler(new[]
        {
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK,
        });
        using var http = new HttpClient(handler);
        var client = new RawPackageContentClient(
            http, "https://example.test/v3/package", maxTransientRetries: 2);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3")));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task RetryAfterLargerThanBudgetDoesNotRetryEarly()
    {
        using var handler = new SequenceHandler(
            new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.OK },
            TimeSpan.FromSeconds(30));
        using var http = new HttpClient(handler);
        var client = new RawPackageContentClient(
            http, "https://example.test/v3/package", maxTransientRetries: 2, maxRetryDelaySeconds: 1);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3")));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task DoesNotRetryPermanentResponses(HttpStatusCode status)
    {
        using var handler = new SequenceHandler(new[] { status, HttpStatusCode.OK });
        using var http = new HttpClient(handler);
        var client = new RawPackageContentClient(
            http, "https://example.test/v3/package", maxTransientRetries: 2);
        if (status == HttpStatusCode.NotFound)
        {
            Assert.Null(await client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3")));
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.DownloadPackageOrNullAsync("Test.Package", NuGetVersion.Parse("1.2.3")));
        }
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CancellationDuringBackoffDoesNotStartAnotherRequest()
    {
        using var cancelled = new CancellationTokenSource();
        using var handler = new SequenceHandler(
            new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.OK },
            TimeSpan.FromSeconds(1), cancelled);
        using var http = new HttpClient(handler);
        var client = new RawPackageContentClient(
            http, "https://example.test/v3/package", maxTransientRetries: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.DownloadPackageOrNullAsync(
                "Test.Package", NuGetVersion.Parse("1.2.3"), cancelled.Token));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;
        private readonly TimeSpan? _retryAfter;
        private readonly CancellationTokenSource _cancelAfterFirst;
        public int Calls { get; private set; }

        public SequenceHandler(
            HttpStatusCode[] statuses,
            TimeSpan? retryAfter = null,
            CancellationTokenSource cancelAfterFirst = null)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
            _retryAfter = retryAfter;
            _cancelAfterFirst = cancelAfterFirst;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1) _cancelAfterFirst?.Cancel();
            var status = _statuses.Dequeue();
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
            };
            if (_retryAfter.HasValue && status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(_retryAfter.Value);
            return Task.FromResult(response);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public StubHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            });
    }
}
