using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Hosting;

internal sealed class LakonaGameRpcConfigurator : IULinkRpcServerConfigurator
{
    private readonly ServerRpcServerOptions _options;
    private readonly Func<IRpcSerializer> _serializerFactory;
    private readonly Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> _acceptorFactory;
    private readonly Action<RpcServiceRegistry, IServiceProvider>? _bindServices;

    public LakonaGameRpcConfigurator(
        ServerRpcServerOptions options,
        Func<IRpcSerializer> serializerFactory,
        Func<ServerRpcServerOptions, Task<IRpcConnectionAcceptor>> acceptorFactory,
        Action<RpcServiceRegistry, IServiceProvider>? bindServices)
    {
        _options = options;
        _serializerFactory = serializerFactory;
        _acceptorFactory = acceptorFactory;
        _bindServices = bindServices;
    }

    public string Name { get; init; } = "default";

    public void Configure(LakonaGameServerRpcContext context)
    {
        var builder = context.Builder;
        builder.UseSerializer(_serializerFactory());
        builder.UseAcceptor(async ct => await _acceptorFactory(_options));
        _bindServices?.Invoke(builder.ServiceRegistry, context.Services);
    }
}
