using Microsoft.Extensions.DependencyInjection;
using Gateway.Generated;
using Gateway.Services;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.WebSocket;

namespace Gateway.Hosting;

internal sealed class DefaultRealtimeRpcServerConfigurator : IRpcServerConfigurator
{
    private readonly ServerRpcServerOptions _options;

    public DefaultRealtimeRpcServerConfigurator(ServerRpcServerOptions options)
    {
        _options = options;
    }

    public string Name => "realtime";

    public void Configure(LakonaGameServerRpcContext context)
    {
        var builder = context.Builder;
        builder.UseSerializer(new MemoryPackRpcSerializer());

        PlayerServiceBinder.Bind(
            builder.ServiceRegistry,
            callback => ActivatorUtilities.CreateInstance<PlayerService>(context.Services, callback));

        var port = _options.Port > 0 ? _options.Port : 20001;
        if (string.Equals(_options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_options.Transport, "ws", StringComparison.OrdinalIgnoreCase))
        {
            var path = string.IsNullOrWhiteSpace(_options.Path) ? "/ws" : _options.Path;
            builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(builder.ResolvePort(port), path, ct));
            return;
        }

        if (string.Equals(_options.Transport, "kcp", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseAcceptor(new KcpConnectionAcceptor(
                builder.ResolvePort(port),
                builder.Limits.MaxPendingAcceptedConnections));
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported realtime transport '{_options.Transport}'. Register a custom {nameof(IRpcServerConfigurator)} for this project.");
    }
}
