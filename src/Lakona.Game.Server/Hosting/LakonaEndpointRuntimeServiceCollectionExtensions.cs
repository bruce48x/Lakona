using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Hosting;

/// <summary>
/// Registers the transport and serializer implementations available to a Lakona game server.
/// </summary>
public static class LakonaEndpointRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers a synchronous client-facing endpoint transport factory under its configuration name.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="name">The transport name used by <c>Lakona:Endpoints[]:Transport</c>.</param>
    /// <param name="factory">The factory that creates an acceptor for one configured endpoint.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddLakonaEndpointTransport(
        this IServiceCollection services,
        string name,
        Func<LakonaGameEndpointOptions, IRpcConnectionAcceptor> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return services.AddLakonaEndpointTransport(
            name,
            (endpoint, _) => new ValueTask<IRpcConnectionAcceptor>(factory(endpoint)));
    }

    /// <summary>
    /// Registers an asynchronous client-facing endpoint transport factory under its configuration name.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="name">The transport name used by <c>Lakona:Endpoints[]:Transport</c>.</param>
    /// <param name="factory">The asynchronous factory that creates an acceptor for one configured endpoint.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddLakonaEndpointTransport(
        this IServiceCollection services,
        string name,
        Func<LakonaGameEndpointOptions, CancellationToken, ValueTask<IRpcConnectionAcceptor>> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        services.TryAddSingleton<LakonaEndpointRuntimeRegistry>();
        services.AddSingleton(new LakonaEndpointTransportRegistration(name, factory));
        return services;
    }

    /// <summary>
    /// Registers a client-facing endpoint serializer factory under its configuration name.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="name">The serializer name used by <c>Lakona:Endpoints[]:Serializer</c>.</param>
    /// <param name="factory">The factory that creates an endpoint serializer.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddLakonaEndpointSerializer(
        this IServiceCollection services,
        string name,
        Func<IRpcSerializer> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        services.TryAddSingleton<LakonaEndpointRuntimeRegistry>();
        services.AddSingleton(new LakonaEndpointSerializerRegistration(name, factory));
        return services;
    }

}
