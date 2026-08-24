using Lakona.Game.Cluster.Membership;

namespace Lakona.Game.Cluster.Rpc.Membership;

internal sealed class MembershipProbeHandler(
    MembershipTableManager manager,
    IClusterMembership membership,
    IMembershipProbeTransport transport) : IMembershipProbeHandler
{
    public async ValueTask<MembershipProbeReply> HandleAsync(
        MembershipProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ClusterMembershipSnapshot snapshot;
        NodeReference local;
        try
        {
            snapshot = membership.Current;
            local = manager.Local;
        }
        catch (InvalidOperationException)
        {
            return new MembershipProbeReply();
        }

        if (request.Cluster != snapshot.Cluster.Value
            || !TryCreateReference(snapshot.Cluster, request.SourceNodeId, request.SourceIncarnation, out var source)
            || !TryCreateReference(snapshot.Cluster, request.TargetNodeId, request.TargetIncarnation, out var target)
            || !await TryRefreshUnknownMemberAsync(source, target, cancellationToken).ConfigureAwait(false))
        {
            return new MembershipProbeReply { MembershipVersion = snapshot.View.Value };
        }

        snapshot = membership.Current;
        if (!snapshot.TryGetMember(source, out _)
            || !snapshot.TryGetMember(target, out var targetMember)
            || targetMember is null)
        {
            return new MembershipProbeReply { MembershipVersion = snapshot.View.Value };
        }

        if (!request.Forward)
        {
            return new MembershipProbeReply
            {
                IsAlive = target == local,
                MembershipVersion = snapshot.View.Value
            };
        }

        if (source == local
            || target == local
            || !string.Equals(targetMember.ClusterEndpoint.Address, request.TargetEndpoint, StringComparison.Ordinal))
        {
            return new MembershipProbeReply { MembershipVersion = snapshot.View.Value };
        }

        var alive = await transport.ProbeAsync(
            local,
            targetMember,
            targetMember.ClusterEndpoint,
            forward: false,
            cancellationToken).ConfigureAwait(false);
        return new MembershipProbeReply
        {
            IsAlive = alive,
            MembershipVersion = snapshot.View.Value
        };
    }

    public async ValueTask<MembershipGossipReply> HandleGossipAsync(
        MembershipGossipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = membership.Current;
        if (request.Cluster != snapshot.Cluster.Value)
        {
            return new MembershipGossipReply();
        }

        if (request.MembershipVersion > snapshot.View.Value)
        {
            await manager.RefreshAsync(cancellationToken).ConfigureAwait(false);
            snapshot = membership.Current;
        }

        if (!TryCreateReference(snapshot.Cluster, request.SourceNodeId, request.SourceIncarnation, out var source)
            || !snapshot.TryGetMember(source, out _))
        {
            return new MembershipGossipReply();
        }

        return new MembershipGossipReply();
    }

    private async ValueTask<bool> TryRefreshUnknownMemberAsync(
        NodeReference source,
        NodeReference target,
        CancellationToken cancellationToken)
    {
        var snapshot = membership.Current;
        if (snapshot.TryGetMember(source, out _) && snapshot.TryGetMember(target, out _))
        {
            return true;
        }

        await manager.RefreshAsync(cancellationToken).ConfigureAwait(false);
        snapshot = membership.Current;
        return snapshot.TryGetMember(source, out _) && snapshot.TryGetMember(target, out _);
    }

    private static bool TryCreateReference(
        ClusterIncarnationId cluster,
        string nodeId,
        Guid incarnation,
        out NodeReference reference)
    {
        try
        {
            reference = new NodeReference(cluster, new NodeId(nodeId), new NodeIncarnationId(incarnation));
            return true;
        }
        catch (ArgumentException)
        {
            reference = null!;
            return false;
        }
    }
}
