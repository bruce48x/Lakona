using System;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcClusterDependencyHealth
    {
        public ULinkRpcClusterDependencyHealth(
            string name,
            ULinkRpcClusterDependencyStatus status,
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

        public ULinkRpcClusterDependencyStatus Status { get; }

        public string? Error { get; }
    }
}
