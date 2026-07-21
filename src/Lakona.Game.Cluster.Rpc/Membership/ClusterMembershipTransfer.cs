using System;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    public sealed class ClusterMembershipTransfer
    {
        private readonly byte[] payload;
        private readonly byte[] checksum;

        internal ClusterMembershipTransfer(MembershipLogSnapshot snapshot)
            : this(
                snapshot.LastIncludedIndex,
                snapshot.LastIncludedTerm,
                snapshot.Payload,
                snapshot.Checksum)
        {
        }

        internal ClusterMembershipTransfer(
            long lastIncludedIndex,
            long lastIncludedTerm,
            ReadOnlyMemory<byte> payload,
            ReadOnlyMemory<byte> checksum)
        {
            LastIncludedIndex = lastIncludedIndex;
            LastIncludedTerm = lastIncludedTerm;
            this.payload = payload.ToArray();
            this.checksum = checksum.ToArray();
        }

        public long LastIncludedIndex { get; }

        public long LastIncludedTerm { get; }

        public ReadOnlyMemory<byte> Payload => payload;

        public ReadOnlyMemory<byte> Checksum => checksum;

        internal MembershipLogSnapshot ToSnapshot()
        {
            return new MembershipLogSnapshot(
                LastIncludedIndex,
                LastIncludedTerm,
                payload,
                checksum);
        }
    }
}
