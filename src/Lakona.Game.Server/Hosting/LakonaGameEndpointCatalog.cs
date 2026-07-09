using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Hosting;

/// <summary>
/// Provides access to the resolved client-facing endpoint configuration.
/// </summary>
public sealed class LakonaGameEndpointCatalog
{
    private readonly IReadOnlyList<LakonaGameEndpointOptions> _endpoints;

    /// <summary>
    /// Initializes a new endpoint catalog.
    /// </summary>
    /// <param name="endpoints">The resolved endpoint options.</param>
    public LakonaGameEndpointCatalog(IReadOnlyList<LakonaGameEndpointOptions> endpoints)
    {
        _endpoints = endpoints;
    }

    /// <summary>
    /// Gets the configured endpoint for a required transport.
    /// </summary>
    /// <param name="transport">The required transport name.</param>
    /// <returns>The endpoint configured for <paramref name="transport"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the transport is not configured.</exception>
    public LakonaGameEndpointOptions RequireTransport(string transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);

        var endpoint = _endpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.Transport, transport, StringComparison.OrdinalIgnoreCase));

        return endpoint ?? throw new InvalidOperationException(
            $"Lakona.Game endpoint transport '{transport}' is required but was not configured.");
    }
}
