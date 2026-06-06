using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Features;

public sealed class LakonaGameEndpointCatalog
{
    private readonly IReadOnlyList<LakonaGameEndpointOptions> _endpoints;

    public LakonaGameEndpointCatalog(IReadOnlyList<LakonaGameEndpointOptions> endpoints)
    {
        _endpoints = endpoints;
    }

    public LakonaGameEndpointOptions RequireTransport(string transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);

        var endpoint = _endpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.Transport, transport, StringComparison.OrdinalIgnoreCase));

        return endpoint ?? throw new InvalidOperationException(
            $"Lakona.Game endpoint transport '{transport}' is required but was not configured.");
    }
}
