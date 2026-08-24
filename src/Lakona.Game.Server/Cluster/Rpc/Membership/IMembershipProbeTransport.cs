namespace Lakona.Game.Cluster.Rpc.Membership;

internal interface IMembershipProbeTransport
{
    ValueTask<bool> ProbeAsync(
        NodeReference source,
        ClusterMember target,
        NodeEndpoint contact,
        bool forward,
        CancellationToken cancellationToken = default);

    ValueTask GossipAsync(
        NodeReference source,
        NodeEndpoint contact,
        MembershipViewId version,
        CancellationToken cancellationToken = default);
}

internal interface IMembershipProbeHandler
{
    ValueTask<MembershipProbeReply> HandleAsync(
        MembershipProbeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<MembershipGossipReply> HandleGossipAsync(
        MembershipGossipRequest request,
        CancellationToken cancellationToken = default);
}
