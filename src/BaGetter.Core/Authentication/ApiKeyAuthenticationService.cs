using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BaGetter.Core;

public class ApiKeyAuthenticationService : IAuthenticationService
{
    private readonly string _apiKey;
    private readonly ApiKey[] _apiKeys;

    public ApiKeyAuthenticationService(IOptionsSnapshot<BaGetterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _apiKey = string.IsNullOrEmpty(options.Value.ApiKey) ? null : options.Value.ApiKey;
        _apiKeys = options.Value.Authentication?.ApiKeys ?? [];
    }

    public Task<bool> AuthenticateAsync(string apiKey, CancellationToken cancellationToken)
        => Task.FromResult(Authenticate(apiKey));

    private bool Authenticate(string apiKey)
    {
        // Never allow package modifications without a configured, nonempty API key.
        // A null input must not match an absent legacy key.
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        return (_apiKey != null && SecretsEqual(_apiKey, apiKey))
            || _apiKeys.Any(x => !string.IsNullOrWhiteSpace(x?.Key) && SecretsEqual(x.Key, apiKey));
    }

    private static bool SecretsEqual(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
