using System;
using System.Threading.Tasks;
using BaGetter.Core;
using BaGetter.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BaGetter.Tests;

public class UpstreamUnavailableMiddlewareTests
{
    [Fact]
    public async Task FailedUpstreamProduces503Not404AndDisablesCaching()
    {
        var context = new DefaultHttpContext();
        var middleware = new UpstreamUnavailableMiddleware(
            _ => throw new UpstreamUnavailableException("origin failure", new InvalidOperationException("secret")),
            NullLogger<UpstreamUnavailableMiddleware>.Instance);

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task UnrelatedExceptionsAreNotHidden()
    {
        var middleware = new UpstreamUnavailableMiddleware(
            _ => throw new ArgumentException("broken code"),
            NullLogger<UpstreamUnavailableMiddleware>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            middleware.Invoke(new DefaultHttpContext()));
    }
}
