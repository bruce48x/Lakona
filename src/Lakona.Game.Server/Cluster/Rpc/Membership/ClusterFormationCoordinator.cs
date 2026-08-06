using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class ClusterFormationCoordinator
    {
        private readonly object gate = new object();
        private readonly NodeId localNode;
        private readonly NodeEndpoint localEndpoint;
        private readonly IClusterMembershipTransport transport;
        private readonly ClusterMembershipNodeOptions options;
        private readonly TimeProvider timeProvider;
        private readonly Dictionary<string, ClusterFormationPeer> knownByNode =
            new Dictionary<string, ClusterFormationPeer>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> nodeByEndpoint =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim membershipRequestGate = new SemaphoreSlim(1, 1);
        private ClusterMembershipNode? established;
        private int started;

        public ClusterFormationCoordinator(
            NodeId localNode,
            NodeEndpoint localEndpoint,
            IReadOnlyList<ClusterFormationPeer> peers,
            IClusterMembershipTransport transport,
            ClusterMembershipNodeOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            this.localNode = localNode;
            this.localEndpoint = localEndpoint
                ?? throw new ArgumentNullException(nameof(localEndpoint));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.options = options ?? new ClusterMembershipNodeOptions();
            this.timeProvider = timeProvider ?? TimeProvider.System;
            MergePeers(new[] { new ClusterFormationPeer(localNode, localEndpoint) });
            MergePeers(peers ?? throw new ArgumentNullException(nameof(peers)));
        }

        public async ValueTask<ClusterMembershipNode> FormOrJoinAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
            {
                throw new InvalidOperationException("Cluster formation can start only once.");
            }

            await Task.Delay(options.MinimumRetryDelay, timeProvider, cancellationToken)
                .ConfigureAwait(false);
            var startedAt = timeProvider.GetTimestamp();
            var retry = options.MinimumRetryDelay;
            var failures = new List<Exception>();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref established) is ClusterMembershipNode current)
                {
                    return current;
                }

                var changed = false;
                var allReachable = true;
                var peers = SnapshotPeers();
                for (var i = 0; i < peers.Count; i++)
                {
                    var peer = peers[i];
                    if (peer.Node == localNode)
                    {
                        continue;
                    }

                    try
                    {
                        var response = MembershipWireCodec.DecodeFormationProbeResponse(
                            await transport.RequestAsync(
                                peer.Endpoint,
                                MembershipWireCodec.EncodeFormationProbeRequest(peers),
                                cancellationToken).ConfigureAwait(false));
                        changed |= MergePeers(response.Peers);
                        if (response.Established)
                        {
                            return Publish(await ClusterMembershipNode.JoinExistingClusterAsync(
                                localNode,
                                localEndpoint,
                                new[] { peer.Endpoint },
                                transport,
                                options,
                                timeProvider,
                                cancellationToken).ConfigureAwait(false));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        allReachable = false;
                        failures.Add(exception);
                    }
                }

                if (!changed && allReachable)
                {
                    peers = SnapshotPeers();
                    var digest = ComputeDigest(peers);
                    var allAccepted = true;
                    for (var i = 0; i < peers.Count; i++)
                    {
                        var peer = peers[i];
                        if (peer.Node == localNode)
                        {
                            continue;
                        }

                        try
                        {
                            var response =
                                MembershipWireCodec.DecodeFormationAgreementResponse(
                                    await transport.RequestAsync(
                                        peer.Endpoint,
                                        MembershipWireCodec.EncodeFormationAgreementRequest(
                                            digest,
                                            peers),
                                        cancellationToken).ConfigureAwait(false));
                            changed |= MergePeers(response.Peers);
                            if (response.Established)
                            {
                                return Publish(
                                    await ClusterMembershipNode.JoinExistingClusterAsync(
                                        localNode,
                                        localEndpoint,
                                        new[] { peer.Endpoint },
                                        transport,
                                        options,
                                        timeProvider,
                                        cancellationToken).ConfigureAwait(false));
                            }

                            allAccepted &= response.Accepted;
                        }
                        catch (OperationCanceledException)
                            when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            allAccepted = false;
                            failures.Add(exception);
                        }
                    }

                    if (!changed
                        && allAccepted
                        && SnapshotPeers()[0].Node == localNode)
                    {
                        return Publish(ClusterMembershipNode.BootstrapNewCluster(
                            localNode,
                            localEndpoint,
                            options,
                            timeProvider));
                    }
                }

                var elapsed = timeProvider.GetElapsedTime(startedAt);
                if (elapsed >= options.JoinRetryWindow)
                {
                    throw new AggregateException(
                        $"Cluster formation did not converge within {options.JoinRetryWindow}. " +
                        "The known peer set was never reduced to form a smaller cluster.",
                        failures);
                }

                var remaining = options.JoinRetryWindow - elapsed;
                await Task.Delay(retry <= remaining ? retry : remaining, timeProvider, cancellationToken)
                    .ConfigureAwait(false);
                retry = retry >= options.MaximumRetryDelay
                    ? options.MaximumRetryDelay
                    : TimeSpan.FromTicks(Math.Min(
                        options.MaximumRetryDelay.Ticks,
                        retry.Ticks * 2));
            }
        }

        public async ValueTask<ClusterMembershipTransportFrame> HandleAsync(
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MembershipWireCodec.IsFormationProbeRequest(request))
            {
                MergePeers(MembershipWireCodec.DecodeFormationProbeRequest(request));
                return MembershipWireCodec.EncodeFormationProbeResponse(
                    Volatile.Read(ref established) is not null,
                    SnapshotPeers());
            }

            if (MembershipWireCodec.IsFormationAgreementRequest(request))
            {
                var agreement = MembershipWireCodec.DecodeFormationAgreementRequest(request);
                MergePeers(agreement.Peers);
                var peers = SnapshotPeers();
                var accepted = Volatile.Read(ref established) is null
                    && string.Equals(
                        agreement.Digest,
                        ComputeDigest(peers),
                        StringComparison.Ordinal);
                return MembershipWireCodec.EncodeFormationAgreementResponse(
                    Volatile.Read(ref established) is not null,
                    accepted,
                    peers);
            }

            var node = Volatile.Read(ref established);
            if (node is null)
            {
                if (MembershipWireCodec.IsJoinRequest(request)
                    || MembershipWireCodec.IsPromoteRequest(request)
                    || MembershipWireCodec.IsReadyRequest(request))
                {
                    return MembershipWireCodec.EncodeNotLeaderResponse(null);
                }

                throw new InvalidOperationException(
                    "Cluster membership is unavailable while formation is incomplete.");
            }
            await membershipRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await node.HandleTransportRequestAsync(
                    request,
                    transport,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                membershipRequestGate.Release();
            }
        }

        private ClusterMembershipNode Publish(ClusterMembershipNode node)
        {
            var existing = Interlocked.CompareExchange(ref established, node, null);
            if (existing is null || ReferenceEquals(existing, node))
            {
                return node;
            }

            if (existing.Membership.Current.Cluster != node.Membership.Current.Cluster)
            {
                throw new InvalidOperationException(
                    "Cluster formation attempted to publish two different incarnations.");
            }

            return existing;
        }

        private bool MergePeers(IEnumerable<ClusterFormationPeer> peers)
        {
            var changed = false;
            lock (gate)
            {
                foreach (var peer in peers)
                {
                    if (knownByNode.TryGetValue(peer.Node.Value, out var byNode))
                    {
                        if (!string.Equals(
                                byNode.Endpoint.Address,
                                peer.Endpoint.Address,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"Formation peer '{peer.Node.Value}' advertises conflicting endpoints.");
                        }

                        continue;
                    }

                    if (nodeByEndpoint.TryGetValue(peer.Endpoint.Address, out var endpointNode)
                        && !string.Equals(endpointNode, peer.Node.Value, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Formation endpoint '{peer.Endpoint.Address}' belongs to conflicting node ids.");
                    }

                    knownByNode.Add(peer.Node.Value, peer);
                    nodeByEndpoint.Add(peer.Endpoint.Address, peer.Node.Value);
                    changed = true;
                }
            }

            return changed;
        }

        private IReadOnlyList<ClusterFormationPeer> SnapshotPeers()
        {
            lock (gate)
            {
                return knownByNode.Values
                    .OrderBy(static peer => peer.Node.Value, StringComparer.Ordinal)
                    .ThenBy(static peer => peer.Endpoint.Address, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        private static string ComputeDigest(IReadOnlyList<ClusterFormationPeer> peers)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < peers.Count; i++)
            {
                builder.Append(peers[i].Node.Value)
                    .Append('\0')
                    .Append(peers[i].Endpoint.Address)
                    .Append('\n');
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        }
    }
}
