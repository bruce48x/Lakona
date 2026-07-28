using System;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeDiscoveryOptions
    {
        public string ClusterName { get; set; } = "local";

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(ClusterName))
            {
                throw new ArgumentException("Cluster name is required.", nameof(ClusterName));
            }
        }
    }
}
