using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Features;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaGameServerBuilder
{
    private Action<RpcServiceRegistry, IServiceProvider>? _serviceBinder;
    private Action<LakonaGameFeatureCatalogBuilder>? _configureFeatures;
    private readonly List<Action<IServiceCollection, IConfiguration>> _serviceRegistrations = new();
    private readonly List<Action<IConfigurationBuilder>> _configActions = new();

    internal IHostApplicationBuilder HostBuilder { get; }

    internal LakonaGameServerBuilder(IHostApplicationBuilder hostBuilder)
    {
        HostBuilder = hostBuilder;
    }

    public LakonaGameServerBuilder AddServices(Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _serviceRegistrations.Add((services, _) => register(services));
        return this;
    }

    public LakonaGameServerBuilder AddServices(Action<IServiceCollection, IConfiguration> register)
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

    public LakonaGameServerBuilder ConfigureFeatures(Action<LakonaGameFeatureCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureFeatures = configure;
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

    internal Action<LakonaGameFeatureCatalogBuilder>? GetFeatureConfiguration() => _configureFeatures;
}
