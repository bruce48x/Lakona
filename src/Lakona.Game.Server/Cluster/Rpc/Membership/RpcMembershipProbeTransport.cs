using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc.Membership;

internal sealed class RpcMembershipProbeTransport(
    IClusterClientFactory clientFactory,
    TimeSpan requestTimeout) : IMembershipProbeTransport
{
    public async ValueTask<bool> ProbeAsync(
        NodeReference source,
        ClusterMember target,
        NodeEndpoint contact,
        bool forward,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(contact);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);
        try
        {
            var client = await clientFactory.GetClientAsync(contact, timeout.Token).ConfigureAwait(false);
            var reply = await client.CallAsync(
                ClusterProtocol.MembershipProbeMethod,
                new MembershipProbeRequest
                {
                    Cluster = source.Cluster.Value,
                    SourceNodeId = source.Node.Value,
                    SourceIncarnation = source.Incarnation.Value,
                    TargetNodeId = target.Reference.Node.Value,
                    TargetIncarnation = target.Reference.Incarnation.Value,
                    TargetEndpoint = target.ClusterEndpoint.Address,
                    Forward = forward
                },
                timeout.Token).ConfigureAwait(false);
            return reply?.IsAlive == true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (RpcException)
        {
            return false;
        }
    }

    public async ValueTask GossipAsync(
        NodeReference source,
        NodeEndpoint contact,
        MembershipViewId version,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);
        var client = await clientFactory.GetClientAsync(contact, timeout.Token).ConfigureAwait(false);
        _ = await client.CallAsync(
            ClusterProtocol.MembershipGossipMethod,
            new MembershipGossipRequest
            {
                Cluster = source.Cluster.Value,
                SourceNodeId = source.Node.Value,
                SourceIncarnation = source.Incarnation.Value,
                MembershipVersion = version.Value
            },
            timeout.Token).ConfigureAwait(false);
    }
}
