using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaEndpointRpcServerConfigurator : IRpcServerConfigurator
{
    private readonly LakonaGameEndpointOptions _endpoint;
    private readonly Func<IRpcSerializer> _serializerFactory;
    private readonly Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> _acceptorFactory;

    public LakonaEndpointRpcServerConfigurator(
        LakonaGameEndpointOptions endpoint,
        Func<IRpcSerializer> serializerFactory,
        Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> acceptorFactory)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _serializerFactory = serializerFactory ?? throw new ArgumentNullException(nameof(serializerFactory));
        _acceptorFactory = acceptorFactory ?? throw new ArgumentNullException(nameof(acceptorFactory));
    }

    public string Transport => _endpoint.Transport;

    public void Configure(LakonaGameServerRpcContext context)
    {
        var builder = context.Builder;
        var options = ToServerOptions(_endpoint);
        builder.UseSerializer(_serializerFactory());
        builder.UseAcceptor(async ct => await _acceptorFactory(options));

        foreach (var observer in context.Services.GetServices<IRpcSessionLifecycleObserver>())
        {
            builder.UseSessionLifecycleObserver(observer);
        }

        var catalog = context.Services.GetRequiredService<LakonaRpcServiceCatalog>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serviceName in _endpoint.RpcServices)
        {
            if (!seen.Add(serviceName))
            {
                throw new InvalidOperationException(
                    $"RPC service '{serviceName}' is configured more than once on endpoint '{_endpoint.Transport}'.");
            }

            if (!catalog.TryGet(serviceName, out var descriptor))
            {
                throw new InvalidOperationException(
                    $"RPC service '{serviceName}' is configured on endpoint '{_endpoint.Transport}' but no binder is registered.");
            }

            var binder = (LakonaRpcServiceBinder)ActivatorUtilities.CreateInstance(context.Services, descriptor.BinderType);
            binder.Bind(context);
        }
    }

    private static ServerRpcServerOptions ToServerOptions(LakonaGameEndpointOptions endpoint)
    {
        return new ServerRpcServerOptions
        {
            Transport = endpoint.Transport,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Path = string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path
        };
    }
}
