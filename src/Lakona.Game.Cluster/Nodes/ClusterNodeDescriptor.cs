using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeDescriptor
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public ClusterNodeDescriptor(
            NodeId node,
            NodeState state,
            IReadOnlyList<NodeServiceDescriptor> services,
            IReadOnlyDictionary<string, string>? labels = null)
        {
            Node = node;
            State = state;
            Services = CopyServices(services);
            Labels = CopyStringDictionary(labels, nameof(labels));
        }

        public NodeId Node { get; }

        public NodeState State { get; }

        public IReadOnlyList<NodeServiceDescriptor> Services { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        internal static ClusterNodeDescriptor FromRecord(NodeRecord record)
        {
            return new ClusterNodeDescriptor(
                record.NodeId,
                record.State,
                record.Services,
                record.Labels);
        }

        private static IReadOnlyList<NodeServiceDescriptor> CopyServices(IReadOnlyList<NodeServiceDescriptor> services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return new ReadOnlyCollection<NodeServiceDescriptor>(new List<NodeServiceDescriptor>(services));
        }

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
