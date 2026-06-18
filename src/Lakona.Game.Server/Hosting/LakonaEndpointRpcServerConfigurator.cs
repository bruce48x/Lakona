using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaEndpointRpcServerConfigurator : IRpcServerConfigurator
{
    private readonly LakonaGameEndpointOptions _endpoint;
    private readonly Action<RpcServiceRegistry, IServiceProvider>? _bindServices;

    public LakonaEndpointRpcServerConfigurator(
        LakonaGameEndpointOptions endpoint,
        Action<RpcServiceRegistry, IServiceProvider>? bindServices = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _bindServices = bindServices;
    }

    public string Transport => _endpoint.Transport;

    public void Configure(LakonaGameServerRpcContext context)
    {
        var builder = context.Builder;
        builder.UseSerializer(LakonaEndpointRuntimeDefaults.CreateSerializer(_endpoint));
        builder.UseAcceptor(ct => LakonaEndpointRuntimeDefaults.CreateAcceptorAsync(_endpoint, ct));

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

        _bindServices?.Invoke(builder.ServiceRegistry, context.Services);
    }
}
