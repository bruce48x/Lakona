using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterMembershipSnapshot
    {
        private readonly IReadOnlyDictionary<NodeReference, ClusterMember> membersByReference;

        public ClusterMembershipSnapshot(
            ClusterIncarnationId cluster,
            MembershipViewId view,
            IReadOnlyList<ClusterMember> members)
        {
            if (cluster.Value == Guid.Empty)
            {
                throw new ArgumentException("Cluster incarnation id is required.", nameof(cluster));
            }

            if (members is null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            var ordered = new List<ClusterMember>(members.Count);
            var byReference = new Dictionary<NodeReference, ClusterMember>();
            var stableNodes = new HashSet<NodeId>();
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i] ?? throw new ArgumentException(
                    "Cluster member cannot be null.",
                    nameof(members));
                if (member.Reference.Cluster != cluster)
                {
                    throw new ArgumentException(
                        "Cluster member belongs to a different cluster incarnation.",
                        nameof(members));
                }

                if (!stableNodes.Add(member.Reference.Node))
                {
                    throw new ArgumentException(
                        "A committed membership view cannot contain multiple incarnations of the same node id.",
                        nameof(members));
                }

                ordered.Add(member);
                byReference.Add(member.Reference, member);
            }

            ordered.Sort(CompareMembers);
            Cluster = cluster;
            View = view;
            Members = new ReadOnlyCollection<ClusterMember>(ordered);
            membersByReference = new ReadOnlyDictionary<NodeReference, ClusterMember>(byReference);
        }

        public ClusterIncarnationId Cluster { get; }

        public MembershipViewId View { get; }

        public IReadOnlyList<ClusterMember> Members { get; }

        public bool TryGetMember(NodeReference reference, out ClusterMember? member)
        {
            if (reference is null)
            {
                member = null;
                return false;
            }

            return membersByReference.TryGetValue(reference, out member);
        }

        private static int CompareMembers(ClusterMember left, ClusterMember right)
        {
            var node = string.Compare(
                left.Reference.Node.Value,
                right.Reference.Node.Value,
                StringComparison.Ordinal);
            return node != 0
                ? node
                : left.Reference.Incarnation.Value.CompareTo(right.Reference.Incarnation.Value);
        }
    }
}
