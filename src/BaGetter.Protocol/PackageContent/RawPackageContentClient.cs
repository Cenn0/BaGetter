using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Protocol.Models;
using NuGet.Versioning;

namespace BaGetter.Protocol.Internal;

/// <summary>
/// The client to interact with an upstream source's Package Content resource.
/// Package and manifest downloads use a bounded retry budget for transient
/// origin HTTP failures. A confirmed 404 is never retried.
/// </summary>
public class RawPackageContentClient : IPackageContentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _packageContentUrl;
    private readonly int _maxTransientRetries;
    private readonly int _maxRetryDelaySeconds;

    public RawPackageContentClient(
        HttpClient httpClient,
        string packageContentUrl,
        int maxTransientRetries = 0,
        int maxRetryDelaySeconds = 5)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _packageContentUrl = packageContentUrl?.TrimEnd('/')
            ?? throw new ArgumentNullException(nameof(packageContentUrl));
        if (maxTransientRetries is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(maxTransientRetries));
        if (maxRetryDelaySeconds is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(maxRetryDelaySeconds));
        _maxTransientRetries = maxTransientRetries;
        _maxRetryDelaySeconds = maxRetryDelaySeconds;
    }

    public async Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var id = packageId.ToLowerInvariant();
        var url = $"{_packageContentUrl}/{id}/index.json";

        return await _httpClient.GetFromJsonOrDefaultAsync<PackageVersionsResponse>(url, cancellationToken);
    }

    public Task<Stream> DownloadPackageOrNullAsync(
        string packageId,
        NuGetVersion packageVersion,
        CancellationToken cancellationToken = default)
    {
        var id = packageId.ToLowerInvariant();
        var version = packageVersion.ToNormalizedString().ToLowerInvariant();
        var url = $"{_packageContentUrl}/{id}/{version}/{id}.{version}.nupkg";
        return DownloadOrNullAsync(url, cancellationToken);
    }

    public Task<Stream> DownloadPackageManifestOrNullAsync(
        string packageId,
        NuGetVersion packageVersion,
        CancellationToken cancellationToken = default)
    {
        var id = packageId.ToLowerInvariant();
        var version = packageVersion.ToNormalizedString().ToLowerInvariant();
        var url = $"{_packageContentUrl}/{id}/{version}/{id}.nuspec";
        return DownloadOrNullAsync(url, cancellationToken);
    }

    private async Task<Stream> DownloadOrNullAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException) when (
                attempt < _maxTransientRetries && !cancellationToken.IsCancellationRequested)
            {
                // Socket/DNS/TLS connection errors can be transient. The request
                // is idempotent and the retry budget prevents unbounded loops.
                await Task.Delay(GetBackoffDelay(attempt), cancellationToken);
                continue;
            }

            try
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Dispose();
                    return null;
                }

                if (IsTransient(response.StatusCode) && attempt < _maxTransientRetries)
                {
                    var wait = GetRetryDelayOrNull(response, attempt);
                    if (wait.HasValue)
                    {
                        response.Dispose();
                        await Task.Delay(wait.Value, cancellationToken);
                        continue;
                    }
                }

                // Never interpret 429, 5xx or other unsuccessful HTTP responses
                // as missing content. A too-large Retry-After ends in the
                // original status, rather than violating the server's hint.
                response.EnsureSuccessStatusCode();

                // The caller owns the returned HTTP content stream. Disposing
                // that stream releases the live HTTP connection.
                return await response.Content.ReadAsStreamAsync(cancellationToken);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    private TimeSpan? GetRetryDelayOrNull(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter != null)
        {
            var requested = retryAfter.Delta ??
                (retryAfter.Date.HasValue
                    ? retryAfter.Date.Value - DateTimeOffset.UtcNow
                    : TimeSpan.Zero);
            if (requested < TimeSpan.Zero) requested = TimeSpan.Zero;

            // Respect a long Retry-After by not immediately retrying at all;
            // a single mirror request has a finite latency budget.
            if (requested > TimeSpan.FromSeconds(_maxRetryDelaySeconds))
                return null;

            return requested;
        }

        return GetBackoffDelay(attempt);
    }

    private TimeSpan GetBackoffDelay(int attempt)
    {
        var baseMs = 200 * (1 << Math.Min(attempt, 4));
        var delayMs = Math.Min(_maxRetryDelaySeconds * 1000,
            baseMs + Random.Shared.Next(0, 100));
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;
}
