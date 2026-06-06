using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Lakona.Game.Cluster
{
    public sealed class NodeRecord
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeRecord(
            string clusterName,
            NodeId nodeId,
            long nodeEpoch,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeServiceDescriptor> services,
            IReadOnlyDictionary<string, string>? labels,
            NodeState state,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset updatedAt)
        {
            if (string.IsNullOrWhiteSpace(clusterName))
            {
                throw new ArgumentException("Cluster name is required.", nameof(clusterName));
            }

            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            ClusterName = clusterName;
            NodeId = nodeId;
            NodeEpoch = nodeEpoch;
            Endpoints = CopyEndpoints(endpoints);
            Services = CopyServices(services);
            Labels = CopyStringDictionary(labels, nameof(labels));
            State = state;
            LeaseExpiresAt = leaseExpiresAt;
            UpdatedAt = updatedAt;
        }

        public string ClusterName { get; }

        public NodeId NodeId { get; }

        public long NodeEpoch { get; }

        public IReadOnlyDictionary<string, NodeEndpoint> Endpoints { get; }

        public IReadOnlyList<NodeServiceDescriptor> Services { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        public NodeState State { get; }

        public DateTimeOffset LeaseExpiresAt { get; }

        public DateTimeOffset UpdatedAt { get; }

        public bool IsExpired(DateTimeOffset now)
        {
            return now >= LeaseExpiresAt;
        }

        public bool HasService(string kind, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Node service kind is required.", nameof(kind));
            }

            if (name is not null && string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Node service name cannot be empty.", nameof(name));
            }

            return Services.Any(service =>
                string.Equals(service.Kind, kind, StringComparison.Ordinal)
                && (name is null || string.Equals(service.Name, name, StringComparison.Ordinal)));
        }

        private static IReadOnlyDictionary<string, NodeEndpoint> CopyEndpoints(
            IReadOnlyDictionary<string, NodeEndpoint> endpoints)
        {
            if (endpoints is null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (endpoints.Count == 0)
            {
                throw new ArgumentException("Node record requires at least one endpoint.", nameof(endpoints));
            }

            var copy = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
            foreach (var endpoint in endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint.Key))
                {
                    throw new ArgumentException("Node endpoint names cannot be empty.", nameof(endpoints));
                }

                copy[endpoint.Key] = endpoint.Value ?? throw new ArgumentException("Node endpoint cannot be null.", nameof(endpoints));
            }

            return new ReadOnlyDictionary<string, NodeEndpoint>(copy);
        }

        private static IReadOnlyList<NodeServiceDescriptor> CopyServices(
            IReadOnlyList<NodeServiceDescriptor> services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (services.Count == 0)
            {
                throw new ArgumentException("Node record requires at least one service.", nameof(services));
            }

            var copy = new List<NodeServiceDescriptor>(services.Count);
            for (var i = 0; i < services.Count; i++)
            {
                copy.Add(services[i] ?? throw new ArgumentException("Node service cannot be null.", nameof(services)));
            }

            return new ReadOnlyCollection<NodeServiceDescriptor>(copy);
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
