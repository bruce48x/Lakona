using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class NodeDirectoryQuery
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeDirectoryQuery(
            string clusterName,
            string? serviceKind = null,
            string? serviceName = null,
            NodeState? state = null,
            IReadOnlyDictionary<string, string>? labels = null,
            bool includeExpired = false)
        {
            if (string.IsNullOrWhiteSpace(clusterName))
            {
                throw new ArgumentException("Cluster name is required.", nameof(clusterName));
            }

            if (serviceKind is not null && string.IsNullOrWhiteSpace(serviceKind))
            {
                throw new ArgumentException("Service kind cannot be empty.", nameof(serviceKind));
            }

            if (serviceName is not null && string.IsNullOrWhiteSpace(serviceName))
            {
                throw new ArgumentException("Service name cannot be empty.", nameof(serviceName));
            }

            ClusterName = clusterName;
            ServiceKind = serviceKind;
            ServiceName = serviceName;
            State = state;
            Labels = CopyStringDictionary(labels, nameof(labels));
            IncludeExpired = includeExpired;
        }

        public string ClusterName { get; }

        public string? ServiceKind { get; }

        public string? ServiceName { get; }

        public NodeState? State { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        public bool IncludeExpired { get; }

        private static IReadOnlyDictionary<string, string> CopyStringDictionary(
            IReadOnlyDictionary<string, string>? source,
            string parameterName)
        {
            if (source is null)
            {
                return EmptyLabels;
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException("Dictionary keys cannot be empty.", parameterName);
                }

                copy[pair.Key] = pair.Value ?? throw new ArgumentException("Dictionary values cannot be null.", parameterName);
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }
    }
}
