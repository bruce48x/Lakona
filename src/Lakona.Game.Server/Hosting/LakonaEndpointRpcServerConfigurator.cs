using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
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
        builder.UseSessionRequestGate(new GameHandshakeRpcGate());
        BindGameFrameworkRpcs(builder.ServiceRegistry, context.Services);

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

    private void BindGameFrameworkRpcs(RpcServiceRegistry registry, IServiceProvider services)
    {
        registry.Register(
            GameHandshakeRpc.ServiceId,
            GameHandshakeRpc.HandshakeMethodId,
            async (session, request, cancellationToken) =>
            {
                var hello = session.Serializer.Deserialize<GameClientHello>(request.Payload.Memory);
                var service = services.GetRequiredService<IGameHandshakeService>();
                var reply = await service.HandshakeAsync(
                    hello,
                    _endpoint.Transport,
                    _endpoint.Serializer,
                    cancellationToken).ConfigureAwait(false);

                var state = session.GetOrAddScopedService(
                    GameHandshakeRpc.ServiceId,
                    static _ => new GameHandshakeSessionState());
                state.IsComplete = true;

                using var payload = session.Serializer.SerializeFrame(reply);
                return RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    RpcStatus.Ok,
                    payload.Memory);
            });

        registry.Register(
            GameHeartbeatRpc.ServiceId,
            GameHeartbeatRpc.HeartbeatMethodId,
            async (session, request, cancellationToken) =>
            {
                var heartbeat = session.Serializer.Deserialize<GameHeartbeatRequest>(request.Payload.Memory);
                var service = services.GetRequiredService<IGameHeartbeatService>();
                var reply = await service.HeartbeatAsync(
                    session.ContextId,
                    heartbeat,
                    cancellationToken).ConfigureAwait(false);

                using var payload = session.Serializer.SerializeFrame(reply);
                return RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    RpcStatus.Ok,
                    payload.Memory);
            });
    }
}
