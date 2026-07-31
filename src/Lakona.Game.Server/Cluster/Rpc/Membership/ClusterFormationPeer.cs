using System;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class ClusterFormationPeer
    {
        public ClusterFormationPeer(NodeId node, NodeEndpoint endpoint)
        {
            Node = node;
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public NodeId Node { get; }

        public NodeEndpoint Endpoint { get; }
    }
}
