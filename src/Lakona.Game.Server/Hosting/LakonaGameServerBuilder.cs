using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Rpc.Server;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hosting;

/// <summary>
/// Configures a Lakona game server host before it is built and run.
/// </summary>
/// <remarks>
/// The builder is used by the high-level hosting entry points to collect user
/// service registrations, app configuration sources, and RPC service bindings
/// while preserving the framework's default startup shape.
/// </remarks>
public sealed class LakonaGameServerBuilder
{
    private Action<RpcServiceRegistry, IServiceProvider>? _serviceBinder;
    private readonly List<Action<IServiceCollection, IConfiguration>> _serviceRegistrations = new();
    private readonly List<Action<IConfigurationBuilder>> _configActions = new();

    internal IHostApplicationBuilder HostBuilder { get; }

    internal LakonaGameServerBuilder(IHostApplicationBuilder hostBuilder)
    {
        HostBuilder = hostBuilder;
    }

    /// <summary>
    /// Adds service registrations to the game server dependency-injection container.
    /// </summary>
    /// <param name="register">The service registration callback.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder AddServices(Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _serviceRegistrations.Add((services, _) => register(services));
        return this;
    }

    /// <summary>
    /// Adds service registrations that can read the host configuration.
    /// </summary>
    /// <param name="register">The service registration callback.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder AddServices(Action<IServiceCollection, IConfiguration> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _serviceRegistrations.Add(register);
        return this;
    }

    /// <summary>
    /// Registers a synchronous client-facing endpoint transport factory under its configuration name.
    /// </summary>
    /// <param name="name">The transport name used by <c>Lakona:Endpoints[]:Transport</c>.</param>
    /// <param name="factory">The factory that creates an acceptor for one configured endpoint.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder RegisterEndpointTransport(
        string name,
        Func<LakonaGameEndpointOptions, IRpcConnectionAcceptor> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddServices(services => services.AddLakonaEndpointTransport(name, factory));
    }

    /// <summary>
    /// Registers an asynchronous client-facing endpoint transport factory under its configuration name.
    /// </summary>
    /// <param name="name">The transport name used by <c>Lakona:Endpoints[]:Transport</c>.</param>
    /// <param name="factory">The asynchronous factory that creates an acceptor for one configured endpoint.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder RegisterEndpointTransport(
        string name,
        Func<LakonaGameEndpointOptions, CancellationToken, ValueTask<IRpcConnectionAcceptor>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddServices(services => services.AddLakonaEndpointTransport(name, factory));
    }

    /// <summary>
    /// Registers a client-facing endpoint serializer factory under its configuration name.
    /// </summary>
    /// <param name="name">The serializer name used by <c>Lakona:Endpoints[]:Serializer</c>.</param>
    /// <param name="factory">The factory that creates an endpoint serializer.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder RegisterEndpointSerializer(string name, Func<IRpcSerializer> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddServices(services => services.AddLakonaEndpointSerializer(name, factory));
    }

    /// <summary>
    /// Adds application configuration sources before framework options are bound.
    /// </summary>
    /// <param name="configure">The configuration callback to apply to the host configuration builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configActions.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds RPC service bindings to the server.
    /// </summary>
    /// <param name="bind">The RPC service registry callback.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder BindServices(Action<RpcServiceRegistry> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        _serviceBinder = (registry, _) => bind(registry);
        return this;
    }

    /// <summary>
    /// Adds RPC service bindings that can resolve services from the built provider.
    /// </summary>
    /// <param name="bind">The RPC service registry callback.</param>
    /// <returns>The same builder for chaining.</returns>
    public LakonaGameServerBuilder BindServices(Action<RpcServiceRegistry, IServiceProvider> bind)
    {
        _serviceBinder = bind ?? throw new ArgumentNullException(nameof(bind));
        return this;
    }

    internal void ApplyToHostBuilder()
    {
        ApplyConfigurationToHostBuilder();
        ApplyServiceRegistrationsToHostBuilder();
    }

    internal void ApplyConfigurationToHostBuilder()
    {
        foreach (var configure in _configActions)
        {
            configure(HostBuilder.Configuration);
        }
    }

    internal void ApplyServiceRegistrationsToHostBuilder()
    {
        foreach (var register in _serviceRegistrations)
        {
            register(HostBuilder.Services, HostBuilder.Configuration);
        }
    }

    internal Action<RpcServiceRegistry, IServiceProvider>? GetServiceBinder() => _serviceBinder;
}
