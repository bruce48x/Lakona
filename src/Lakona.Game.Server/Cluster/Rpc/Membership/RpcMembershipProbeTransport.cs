using System.Net.Sockets;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc.Membership;

internal sealed class RpcMembershipProbeTransport(
    IClusterClientFactory clientFactory,
    TimeSpan requestTimeout) : IMembershipProbeTransport
{
    public async ValueTask<MembershipProbeStatus> ProbeAsync(
        NodeReference source,
        ClusterMember target,
        NodeEndpoint contact,
        bool forward,
        TimeSpan? probeTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(contact);
        cancellationToken.ThrowIfCancellationRequested();
        var targetTimeout = probeTimeout ?? requestTimeout;
        var callTimeout = forward ? targetTimeout * 2 : targetTimeout;
        if (targetTimeout <= TimeSpan.Zero || callTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(probeTimeout));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Forwarding needs time for the helper's probe and its reply.
        timeout.CancelAfter(callTimeout);
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
                    Forward = forward,
                    TargetProbeTimeout = targetTimeout
                },
                timeout.Token).ConfigureAwait(false);
            return reply?.Status switch
            {
                MembershipProbeStatus.Succeeded => MembershipProbeStatus.Succeeded,
                MembershipProbeStatus.Failed => MembershipProbeStatus.Failed,
                _ => MembershipProbeStatus.Unknown
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return forward ? MembershipProbeStatus.Unknown : MembershipProbeStatus.Failed;
        }
        catch (Exception exception) when (exception is TimeoutException or SocketException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return forward ? MembershipProbeStatus.Unknown : MembershipProbeStatus.Failed;
        }
        catch (RpcException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A remote handler error or rejection is not a completed target probe.
            return MembershipProbeStatus.Unknown;
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
