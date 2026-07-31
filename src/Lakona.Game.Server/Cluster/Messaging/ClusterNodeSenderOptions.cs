using System;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeSenderOptions
    {
        public string EndpointName { get; set; } = "cluster";

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(EndpointName))
            {
                throw new InvalidOperationException("Cluster node sender endpoint name is required.");
            }
        }
    }
}
