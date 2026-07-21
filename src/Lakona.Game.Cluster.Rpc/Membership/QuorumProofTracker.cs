using System;
using System.Threading;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class QuorumProofTracker
    {
        private readonly IClusterMembership membership;
        private readonly TimeProvider timeProvider;
        private readonly TimeSpan maximumValidity;
        private AcceptedProof? accepted;

        public QuorumProofTracker(
            IClusterMembership membership,
            TimeProvider timeProvider,
            TimeSpan maximumValidity)
        {
            this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
            this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (maximumValidity <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumValidity),
                    "Maximum quorum proof validity must be positive.");
            }

            this.maximumValidity = maximumValidity;
        }

        public bool HasAuthority
        {
            get
            {
                var current = Volatile.Read(ref accepted);
                if (current is null)
                {
                    return false;
                }

                var snapshot = membership.Current;
                if (snapshot.Cluster != current.Proof.Cluster
                    || snapshot.View != current.Proof.View)
                {
                    return false;
                }

                var elapsed = timeProvider.GetElapsedTime(
                    current.AcceptedAt,
                    timeProvider.GetTimestamp());
                return elapsed < current.Proof.ValidFor;
            }
        }

        public TimeSpan RemainingAuthority
        {
            get
            {
                var current = Volatile.Read(ref accepted);
                if (current is null)
                {
                    return TimeSpan.Zero;
                }

                var snapshot = membership.Current;
                if (snapshot.Cluster != current.Proof.Cluster
                    || snapshot.View != current.Proof.View)
                {
                    return TimeSpan.Zero;
                }

                var elapsed = timeProvider.GetElapsedTime(
                    current.AcceptedAt,
                    timeProvider.GetTimestamp());
                var remaining = current.Proof.ValidFor - elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        public bool TryAccept(QuorumProof proof)
        {
            if (proof is null)
            {
                throw new ArgumentNullException(nameof(proof));
            }

            var snapshot = membership.Current;
            if (proof.Cluster != snapshot.Cluster
                || proof.View != snapshot.View
                || proof.ValidFor > maximumValidity)
            {
                return false;
            }

            while (true)
            {
                var current = Volatile.Read(ref accepted);
                if (current is not null
                    && (proof.Term < current.Proof.Term
                        || proof.Term == current.Proof.Term
                        && proof.Sequence <= current.Proof.Sequence))
                {
                    return false;
                }

                var next = new AcceptedProof(proof, timeProvider.GetTimestamp());
                if (Interlocked.CompareExchange(ref accepted, next, current) == current)
                {
                    return true;
                }
            }
        }

        private sealed class AcceptedProof
        {
            public AcceptedProof(QuorumProof proof, long acceptedAt)
            {
                Proof = proof;
                AcceptedAt = acceptedAt;
            }

            public QuorumProof Proof { get; }

            public long AcceptedAt { get; }
        }
    }
}
