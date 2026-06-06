using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaGameServerBuilder
{
    private Func<IRpcSerializer>? _serializerFactory;
    private Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>>? _acceptorFactory;
    private Action<RpcServiceRegistry, IServiceProvider>? _serviceBinder;
    private string _transport = "websocket";
    private Action<LakonaGameFeatureCatalogBuilder>? _configureFeatures;
    private readonly List<Action<IServiceCollection>> _serviceRegistrations = new();
    private readonly List<Action<IConfigurationBuilder>> _configActions = new();
    private readonly List<RpcEndpointRegistration> _additionalEndpoints = new();

    internal IHostApplicationBuilder HostBuilder { get; }

    internal LakonaGameServerBuilder(IHostApplicationBuilder hostBuilder)
    {
        HostBuilder = hostBuilder;
    }

    public LakonaGameServerBuilder AddServices(Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _serviceRegistrations.Add(register);
        return this;
    }

    public LakonaGameServerBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configActions.Add(configure);
        return this;
    }

    public LakonaGameServerBuilder UseTransport(string transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        _transport = transport;
        return this;
    }

    public LakonaGameServerBuilder ConfigureFeatures(Action<LakonaGameFeatureCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureFeatures = configure;
        return this;
    }

    public LakonaGameServerBuilder UseSerializer(Func<IRpcSerializer> factory)
    {
        _serializerFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public LakonaGameServerBuilder UseAcceptor(Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> factory)
    {
        _acceptorFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public LakonaGameServerBuilder BindServices(Action<RpcServiceRegistry> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        _serviceBinder = (registry, _) => bind(registry);
        return this;
    }

    public LakonaGameServerBuilder BindServices(Action<RpcServiceRegistry, IServiceProvider> bind)
    {
        _serviceBinder = bind ?? throw new ArgumentNullException(nameof(bind));
        return this;
    }

    public LakonaGameServerBuilder AddRpcEndpoint(
        string name,
        string transport,
        Func<IRpcSerializer> serializerFactory,
        Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> acceptorFactory,
        Action<RpcServiceRegistry>? serviceBinder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentNullException.ThrowIfNull(serializerFactory);
        ArgumentNullException.ThrowIfNull(acceptorFactory);

        _additionalEndpoints.Add(new RpcEndpointRegistration(
            name,
            transport,
            serializerFactory,
            acceptorFactory,
            serviceBinder is null ? null : (registry, _) => serviceBinder(registry)));
        return this;
    }

    public LakonaGameServerBuilder AddRpcEndpoint(
        string name,
        string transport,
        Func<IRpcSerializer> serializerFactory,
        Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> acceptorFactory,
        Action<RpcServiceRegistry, IServiceProvider>? serviceBinder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentNullException.ThrowIfNull(serializerFactory);
        ArgumentNullException.ThrowIfNull(acceptorFactory);

        _additionalEndpoints.Add(new RpcEndpointRegistration(
            name, transport, serializerFactory, acceptorFactory, serviceBinder));
        return this;
    }

    internal void ApplyToHostBuilder()
    {
        foreach (var register in _serviceRegistrations)
        {
            register(HostBuilder.Services);
        }
    }

    internal Func<IRpcSerializer> GetSerializerFactory()
    {
        return _serializerFactory ?? throw new InvalidOperationException(
            "Serializer factory is required. Call UseSerializer() before RunAsync().");
    }

    internal Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> GetAcceptorFactory()
    {
        return _acceptorFactory ?? throw new InvalidOperationException(
            "Acceptor factory is required. Call UseAcceptor() before RunAsync().");
    }

    internal Action<RpcServiceRegistry, IServiceProvider>? GetServiceBinder() => _serviceBinder;

    internal string GetTransport() => _transport;

    internal Action<LakonaGameFeatureCatalogBuilder>? GetFeatureConfiguration() => _configureFeatures;

    internal IReadOnlyList<RpcEndpointRegistration> GetAdditionalEndpoints() => _additionalEndpoints;

    internal sealed record RpcEndpointRegistration(
        string Name,
        string Transport,
        Func<IRpcSerializer> SerializerFactory,
        Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> AcceptorFactory,
        Action<RpcServiceRegistry, IServiceProvider>? ServiceBinder);
}
