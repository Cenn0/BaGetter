using System;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BaGetter.Web;

/// <summary>Keep transient upstream errors distinct from missing package versions.</summary>
public sealed class UpstreamUnavailableMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UpstreamUnavailableMiddleware> _logger;

    public UpstreamUnavailableMiddleware(
        RequestDelegate next,
        ILogger<UpstreamUnavailableMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UpstreamUnavailableException ex) when (!context.Response.HasStarted)
        {
            _logger.LogWarning(ex, "Upstream package source temporarily unavailable");
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync("Upstream package source temporarily unavailable.");
        }
    }
}
