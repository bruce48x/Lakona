using System;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeSenderOptions
    {
        public string ClusterName { get; set; } = "local";

        public string EndpointName { get; set; } = "cluster";

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(ClusterName))
            {
                throw new InvalidOperationException("Cluster node sender cluster name is required.");
            }

            if (string.IsNullOrWhiteSpace(EndpointName))
            {
                throw new InvalidOperationException("Cluster node sender endpoint name is required.");
            }
        }
    }
}
