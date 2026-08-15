using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    internal sealed class ClusterMembershipState : IClusterMembership
    {
        private readonly object gate = new object();
        private readonly List<Waiter> waiters = new List<Waiter>();
        private ClusterMembershipSnapshot? current;

        public ClusterMembershipState()
        {
        }

        public ClusterMembershipState(ClusterMembershipSnapshot initial)
        {
            current = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        public ClusterMembershipSnapshot Current
        {
            get
            {
                return Volatile.Read(ref current) ?? throw NotInitialized();
            }
        }

        public async ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Waiter waiter;
            lock (gate)
            {
                var snapshot = current ?? throw NotInitialized();
                if (snapshot.View.CompareTo(after) > 0)
                {
                    return snapshot;
                }

                waiter = new Waiter(after);
                waiters.Add(waiter);
            }

            using (cancellationToken.Register(
                static state =>
                {
                    var registration = (WaiterRegistration)state!;
                    registration.Owner.Cancel(registration.Waiter, registration.CancellationToken);
                },
                new WaiterRegistration(this, waiter, cancellationToken)))
            {
                return await waiter.Completion.Task.ConfigureAwait(false);
            }
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
            Initialize(new ClusterMembershipSnapshot(
                cluster,
                new MembershipViewId(1),
                new[] { member }));
        }

        internal void PublishCommitted(ClusterMembershipSnapshot next)
        {
            ArgumentNullException.ThrowIfNull(next);

            List<Waiter>? completed;
            lock (gate)
            {
                var snapshot = current ?? throw NotInitialized();
                if (next.Cluster != snapshot.Cluster)
                {
                    throw new InvalidOperationException(
                        "A committed membership entry cannot replace the cluster incarnation.");
                }

                if (snapshot.View.Value == long.MaxValue
                    || next.View.Value != snapshot.View.Value + 1)
                {
                    throw new InvalidOperationException(
                        "A committed membership entry must publish exactly the next membership view.");
                }

                completed = PublishLocked(next);
            }

            CompleteWaiters(completed, next);
        }

        internal void InitializeFromCommitted(ClusterMembershipSnapshot restored)
        {
            ArgumentNullException.ThrowIfNull(restored);
            Initialize(restored);
        }

        internal void RestoreCommitted(ClusterMembershipSnapshot restored)
        {
            ArgumentNullException.ThrowIfNull(restored);

            List<Waiter>? completed = null;
            lock (gate)
            {
                var snapshot = current ?? throw NotInitialized();

                if (restored.Cluster != snapshot.Cluster)
                {
                    throw new InvalidOperationException(
                        "A committed snapshot cannot replace the cluster incarnation.");
                }

                if (restored.View.CompareTo(snapshot.View) < 0)
                {
                    throw new InvalidOperationException(
                        "A committed snapshot cannot move membership to an older view.");
                }

                if (restored.View != snapshot.View)
                {
                    completed = PublishLocked(restored);
                }
            }

            CompleteWaiters(completed, restored);
        }

        private void Initialize(ClusterMembershipSnapshot initial)
        {
            lock (gate)
            {
                if (current is not null)
                {
                    throw new InvalidOperationException(
                        "Cluster membership has already bootstrapped or joined a cluster.");
                }

                Volatile.Write(ref current, initial);
            }
        }

        private List<Waiter>? PublishLocked(ClusterMembershipSnapshot next)
        {
            Volatile.Write(ref current, next);
            List<Waiter>? completed = null;
            for (var i = waiters.Count - 1; i >= 0; i--)
            {
                var waiter = waiters[i];
                if (next.View.CompareTo(waiter.After) <= 0)
                {
                    continue;
                }

                waiters.RemoveAt(i);
                (completed ??= new List<Waiter>()).Add(waiter);
            }

            return completed;
        }

        private static void CompleteWaiters(
            List<Waiter>? completed,
            ClusterMembershipSnapshot snapshot)
        {
            if (completed is null)
            {
                return;
            }

            for (var i = 0; i < completed.Count; i++)
            {
                completed[i].Completion.TrySetResult(snapshot);
            }
        }

        private static InvalidOperationException NotInitialized()
        {
            return new InvalidOperationException(
                "Cluster membership has not bootstrapped or joined a cluster.");
        }

        private void Cancel(Waiter waiter, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                waiters.Remove(waiter);
            }

            waiter.Completion.TrySetCanceled(cancellationToken);
        }

        private sealed class Waiter
        {
            public Waiter(MembershipViewId after)
            {
                After = after;
                Completion = new TaskCompletionSource<ClusterMembershipSnapshot>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public MembershipViewId After { get; }

            public TaskCompletionSource<ClusterMembershipSnapshot> Completion { get; }
        }

        private sealed class WaiterRegistration
        {
            public WaiterRegistration(
                ClusterMembershipState owner,
                Waiter waiter,
                CancellationToken cancellationToken)
            {
                Owner = owner;
                Waiter = waiter;
                CancellationToken = cancellationToken;
            }

            public ClusterMembershipState Owner { get; }

            public Waiter Waiter { get; }

            public CancellationToken CancellationToken { get; }
        }
    }
}
