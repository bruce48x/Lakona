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

            return QueryAsync(
                new ClusterNodeDiscoveryQuery(labels: labels),
                cancellationToken);
        }

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> QueryAsync(
            ClusterNodeDiscoveryQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = membership.Current;
            var matches = new List<ClusterNodeDescriptor>();
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                var member = snapshot.Members[i];
                var descriptor = ClusterNodeDescriptor.FromMember(member);
                if (query.Matches(descriptor))
                {
                    matches.Add(descriptor);
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

    }
}
