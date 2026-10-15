using System;

namespace BaGetter.Core;

/// <summary>
/// A transport, protocol, or storage error while resolving an upstream package.
/// Unlike a genuine upstream 404, the package has not been proven absent.
/// </summary>
public sealed class UpstreamUnavailableException : Exception
{
    public UpstreamUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
