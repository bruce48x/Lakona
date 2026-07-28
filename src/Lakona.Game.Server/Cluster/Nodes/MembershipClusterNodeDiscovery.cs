using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class MembershipClusterNodeDiscovery : IClusterNodeDiscovery
    {
        private readonly IClusterMembership membership;

        public MembershipClusterNodeDiscovery(IClusterMembership membership)
        {
            this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        }

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default)
        {
            if (labels is null)
            {
                throw new ArgumentNullException(nameof(labels));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = membership.Current;
            var matches = new List<ClusterNodeDescriptor>();
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                var member = snapshot.Members[i];
                if (member.State == ClusterMemberState.Ready && Matches(member, labels))
                {
                    matches.Add(ClusterNodeDescriptor.FromMember(member));
                }
            }

            return new ValueTask<IReadOnlyList<ClusterNodeDescriptor>>(matches.ToArray());
        }

        public async ValueTask<ClusterNodeDescriptor?> AnyAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default)
        {
            var nodes = await ListAsync(labels, cancellationToken).ConfigureAwait(false);
            return nodes.Count == 0 ? null : nodes[0];
        }

        private static bool Matches(
            ClusterMember member,
            IReadOnlyDictionary<string, string> required)
        {
            foreach (var pair in required)
            {
                if (!member.Labels.TryGetValue(pair.Key, out var value)
                    || !string.Equals(value, pair.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
