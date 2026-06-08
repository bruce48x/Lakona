using System;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterDependencyHealth
    {
        public ClusterDependencyHealth(
            string name,
            ClusterDependencyStatus status,
            string? error = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Dependency name is required.", nameof(name));
            }

            Name = name;
            Status = status;
            Error = error;
        }

        public string Name { get; }

        public ClusterDependencyStatus Status { get; }

        public string? Error { get; }
    }
}
