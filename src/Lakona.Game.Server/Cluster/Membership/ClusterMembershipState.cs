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
        private ClusterMembershipSnapshot current;

        public ClusterMembershipState(ClusterMembershipSnapshot initial)
        {
            current = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        public ClusterMembershipSnapshot Current
        {
            get { return Volatile.Read(ref current); }
        }

        public async ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Waiter waiter;
            lock (gate)
            {
                var snapshot = current;
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

        internal void Publish(ClusterMembershipSnapshot next)
        {
            if (next is null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            List<Waiter>? completed = null;
            lock (gate)
            {
                if (next.Cluster != current.Cluster)
                {
                    throw new InvalidOperationException(
                        "A membership state cannot publish a different cluster incarnation.");
                }

                if (next.View.CompareTo(current.View) <= 0)
                {
                    throw new InvalidOperationException(
                        "A membership state can only publish a newer committed view.");
                }

                Volatile.Write(ref current, next);
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
            }

            if (completed is null)
            {
                return;
            }

            for (var i = 0; i < completed.Count; i++)
            {
                completed[i].Completion.TrySetResult(next);
            }
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
