using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hosting;

internal sealed record LakonaEndpointTransportRegistration(
    string Name,
    Func<LakonaGameEndpointOptions, CancellationToken, ValueTask<IRpcConnectionAcceptor>> Factory);

internal sealed record LakonaEndpointSerializerRegistration(
    string Name,
    Func<IRpcSerializer> Factory);

internal sealed class LakonaEndpointRuntimeRegistry
{
    private readonly IReadOnlyDictionary<string, LakonaEndpointTransportRegistration> _transports;
    private readonly IReadOnlyDictionary<string, LakonaEndpointSerializerRegistration> _endpointSerializers;

    public LakonaEndpointRuntimeRegistry(
        IEnumerable<LakonaEndpointTransportRegistration> transports,
        IEnumerable<LakonaEndpointSerializerRegistration> endpointSerializers)
    {
        _transports = BuildRegistry(transports, static registration => registration.Name, "endpoint transport");
        _endpointSerializers = BuildRegistry(
            endpointSerializers,
            static registration => registration.Name,
            "endpoint serializer");
    }

    public IRpcSerializer CreateEndpointSerializer(LakonaGameEndpointOptions endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var name = Normalize(endpoint.Serializer);
        if (!_endpointSerializers.TryGetValue(name, out var registration))
        {
            throw MissingRegistration("endpoint serializer", name);
        }

        return registration.Factory()
            ?? throw new InvalidOperationException($"Endpoint serializer factory '{name}' returned null.");
    }

    public async ValueTask<IRpcConnectionAcceptor> CreateAcceptorAsync(
        LakonaGameEndpointOptions endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var name = Normalize(endpoint.Transport);
        if (!_transports.TryGetValue(name, out var registration))
        {
            throw MissingRegistration("endpoint transport", name);
        }

        return await registration.Factory(endpoint, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Endpoint transport factory '{name}' returned null.");
    }

    private static IReadOnlyDictionary<string, TRegistration> BuildRegistry<TRegistration>(
        IEnumerable<TRegistration> registrations,
        Func<TRegistration, string> getName,
        string kind)
    {
        var result = new Dictionary<string, TRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            var name = Normalize(getName(registration));
            if (!result.TryAdd(name, registration))
            {
                throw new InvalidOperationException($"The {kind} '{name}' is registered more than once.");
            }
        }

        return result;
    }

    private static InvalidOperationException MissingRegistration(string kind, string name)
    {
        return new InvalidOperationException(
            $"The configured {kind} '{name}' is not registered. Register the implementation during server startup.");
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
    }
}
