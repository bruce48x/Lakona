using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    internal sealed class ClusterMembershipRuntime : IClusterMembership
    {
        private readonly object gate = new object();
        private ClusterMembershipState? state;

        public ClusterMembershipSnapshot Current
        {
            get
            {
                var initialized = Volatile.Read(ref state);
                if (initialized is null)
                {
                    throw new InvalidOperationException(
                        "Cluster membership has not bootstrapped or joined a cluster.");
                }

                return initialized.Current;
            }
        }

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            var initialized = Volatile.Read(ref state);
            if (initialized is null)
            {
                throw new InvalidOperationException(
                    "Cluster membership has not bootstrapped or joined a cluster.");
            }

            return initialized.WaitForChangeAsync(after, cancellationToken);
        }

        internal void BootstrapNewCluster(
            NodeId node,
            NodeIncarnationId incarnation,
            NodeEndpoint clusterEndpoint)
        {
            if (clusterEndpoint is null)
            {
                throw new ArgumentNullException(nameof(clusterEndpoint));
            }

            var cluster = ClusterIncarnationId.New();
            var reference = new NodeReference(cluster, node, incarnation);
            var member = new ClusterMember(
                reference,
                ClusterMemberState.Recovering,
                clusterEndpoint,
                isVoter: true);
            var initial = new ClusterMembershipSnapshot(
                cluster,
                new MembershipViewId(1),
                new[] { member });
            var initialized = new ClusterMembershipState(initial);

            lock (gate)
            {
                if (state is not null)
                {
                    throw new InvalidOperationException(
                        "Cluster membership has already bootstrapped or joined a cluster.");
                }

                Volatile.Write(ref state, initialized);
            }
        }

        internal void PublishCommitted(ClusterMembershipSnapshot next)
        {
            if (next is null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            lock (gate)
            {
                var initialized = state;
                if (initialized is null)
                {
                    throw new InvalidOperationException(
                        "Cluster membership has not bootstrapped or joined a cluster.");
                }

                var current = initialized.Current;
                if (next.Cluster != current.Cluster)
                {
                    throw new InvalidOperationException(
                        "A committed membership entry cannot replace the cluster incarnation.");
                }

                if (current.View.Value == long.MaxValue
                    || next.View.Value != current.View.Value + 1)
                {
                    throw new InvalidOperationException(
                        "A committed membership entry must publish exactly the next membership view.");
                }

                initialized.Publish(next);
            }
        }

        internal void RestoreCommitted(ClusterMembershipSnapshot restored)
        {
            if (restored is null)
            {
                throw new ArgumentNullException(nameof(restored));
            }

            lock (gate)
            {
                var initialized = state;
                if (initialized is null)
                {
                    Volatile.Write(ref state, new ClusterMembershipState(restored));
                    return;
                }

                var current = initialized.Current;
                if (restored.Cluster != current.Cluster)
                {
                    throw new InvalidOperationException(
                        "A committed snapshot cannot replace the cluster incarnation.");
                }

                if (restored.View.CompareTo(current.View) < 0)
                {
                    throw new InvalidOperationException(
                        "A committed snapshot cannot move membership to an older view.");
                }

                if (restored.View != current.View)
                {
                    initialized.Publish(restored);
                }
            }
        }
    }
}
