using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc.Membership;

internal sealed class MembershipProbeBinder
{
    private readonly IMembershipProbeHandler handler;

    private MembershipProbeBinder(IMembershipProbeHandler handler)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public static void Bind(RpcServiceRegistry registry, IMembershipProbeHandler handler) =>
        new MembershipProbeBinder(handler).Bind(registry);

    private void Bind(RpcServiceRegistry registry)
    {
        var service = registry.RegisterSingleton(
            ClusterProtocol.ServiceId,
            this,
            serviceName: nameof(MembershipProbeBinder));
        service.Register<MembershipProbeRequest, MembershipProbeReply>(
            ClusterProtocol.Methods.MembershipProbe,
            static (binder, request, cancellationToken) => binder.handler.HandleAsync(request, cancellationToken),
            methodName: nameof(IMembershipProbeHandler.HandleAsync));
        service.Register<MembershipGossipRequest, MembershipGossipReply>(
            ClusterProtocol.Methods.MembershipGossip,
            static (binder, request, cancellationToken) => binder.handler.HandleGossipAsync(request, cancellationToken),
            methodName: nameof(IMembershipProbeHandler.HandleGossipAsync));
    }
}
