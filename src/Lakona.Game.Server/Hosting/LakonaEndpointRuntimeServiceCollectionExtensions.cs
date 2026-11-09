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
        services.AddLakonaEndpointRuntimeRegistry();
        services.AddSingleton(new LakonaEndpointTransportRegistration(name, factory));
        return services;
    }

    /// <summary>
    /// Registers a client-facing endpoint serializer factory under its configuration name.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="name">The wire profile used by <c>Lakona:Endpoints[]:Serializer</c>. Register the same wire options under this name on every node that sends notifications to these clients.</param>
    /// <param name="factory">Creates the serializer shared by this host's endpoints and notifications for the profile. The serializer must support concurrent calls. Returning an instance transfers its disposal ownership to this host; do not return an instance owned by another container or host.</param>
    /// <remarks>Aliases returning the same instance share one cleanup. Async host disposal prefers IAsyncDisposable; an async-only serializer requires disposing the host asynchronously.</remarks>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddLakonaEndpointSerializer(
        this IServiceCollection services,
        string name,
        Func<IRpcSerializer> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddLakonaEndpointRuntimeRegistry();
        services.AddSingleton(new LakonaEndpointSerializerRegistration(name, factory));
        return services;
    }

    internal static void AddLakonaEndpointRuntimeRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton(static _ => new LakonaEndpointSerializerOwner());
        services.TryAddSingleton<LakonaEndpointRuntimeRegistry>();
    }
}
