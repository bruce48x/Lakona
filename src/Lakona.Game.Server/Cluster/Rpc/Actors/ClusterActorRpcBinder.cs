using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Actors;

internal static class ClusterActorRpcBinder
{
    public static void Bind(
        RpcServiceRegistry registry,
        HotfixActorClusterHandler handler)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(handler);

        registry.RegisterRawWriter(
            ClusterProtocol.ServiceId,
            ClusterProtocol.Methods.ActorAsk,
            (_, _, payload, response, cancellationToken) =>
                handler.HandleActorRpcAsync(
                    payload,
                    tell: false,
                    response,
                    cancellationToken),
            serviceName: "ClusterActor",
            methodName: "Ask");
        registry.RegisterRawWriter(
            ClusterProtocol.ServiceId,
            ClusterProtocol.Methods.ActorTell,
            (_, _, payload, response, cancellationToken) =>
                handler.HandleActorRpcAsync(
                    payload,
                    tell: true,
                    response,
                    cancellationToken),
            serviceName: "ClusterActor",
            methodName: "Tell");
        registry.RegisterRawWriter(
            ClusterProtocol.ServiceId,
            ClusterProtocol.Methods.ActorCancel,
            (_, _, payload, response, _) =>
            {
                handler.HandleActorCancellationRpc(payload, response);
                return ValueTask.CompletedTask;
            },
            serviceName: "ClusterActor",
            methodName: "Cancel");
    }
}
