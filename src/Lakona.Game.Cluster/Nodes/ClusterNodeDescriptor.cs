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
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeFeatureDescriptor> features,
            IReadOnlyDictionary<string, string>? labels = null)
            : this(node, state, endpoints, features, Array.Empty<NodeActorHostDescriptor>(), labels)
        {
        }

        public ClusterNodeDescriptor(
            NodeId node,
            NodeState state,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeActorHostDescriptor> actorHosts,
            IReadOnlyDictionary<string, string>? labels = null)
            : this(node, state, endpoints, Array.Empty<NodeFeatureDescriptor>(), actorHosts, labels)
        {
        }

        public ClusterNodeDescriptor(
            NodeId node,
            NodeState state,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeFeatureDescriptor> features,
            IReadOnlyList<NodeActorHostDescriptor> actorHosts,
            IReadOnlyDictionary<string, string>? labels = null)
        {
            Node = node;
            State = state;
            Endpoints = CopyEndpoints(endpoints);
            Features = CopyFeatures(features);
            ActorHosts = CopyActorHosts(actorHosts);
            Labels = CopyStringDictionary(labels, nameof(labels));
        }

        public NodeId Node { get; }

        public NodeState State { get; }

        public IReadOnlyDictionary<string, NodeEndpoint> Endpoints { get; }

        public IReadOnlyList<NodeFeatureDescriptor> Features { get; }

        public IReadOnlyList<NodeActorHostDescriptor> ActorHosts { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        internal static ClusterNodeDescriptor FromRecord(NodeRecord record)
        {
            return new ClusterNodeDescriptor(
                record.NodeId,
                record.State,
                record.Endpoints,
                record.Features,
                record.ActorHosts,
                record.Labels);
        }

        private static IReadOnlyDictionary<string, NodeEndpoint> CopyEndpoints(
            IReadOnlyDictionary<string, NodeEndpoint> endpoints)
        {
            if (endpoints is null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            var copy = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
            foreach (var endpoint in endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint.Key))
                {
                    throw new ArgumentException("Node endpoint names cannot be empty.", nameof(endpoints));
                }

                copy[endpoint.Key] = endpoint.Value ?? throw new ArgumentException(
                    "Node endpoint cannot be null.",
                    nameof(endpoints));
            }

            return new ReadOnlyDictionary<string, NodeEndpoint>(copy);
        }

        private static IReadOnlyList<NodeFeatureDescriptor> CopyFeatures(
            IReadOnlyList<NodeFeatureDescriptor> features)
        {
            if (features is null)
            {
                throw new ArgumentNullException(nameof(features));
            }

            return new ReadOnlyCollection<NodeFeatureDescriptor>(new List<NodeFeatureDescriptor>(features));
        }

        private static IReadOnlyList<NodeActorHostDescriptor> CopyActorHosts(
            IReadOnlyList<NodeActorHostDescriptor> actorHosts)
        {
            if (actorHosts is null)
            {
                throw new ArgumentNullException(nameof(actorHosts));
            }

            var copy = new List<NodeActorHostDescriptor>(actorHosts.Count);
            for (var i = 0; i < actorHosts.Count; i++)
            {
                copy.Add(actorHosts[i] ?? throw new ArgumentException("Node actor host cannot be null.", nameof(actorHosts)));
            }

            return new ReadOnlyCollection<NodeActorHostDescriptor>(copy);
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
