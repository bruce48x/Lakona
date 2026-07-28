using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class NodeRegistration
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeRegistration(
            string clusterName,
            NodeId nodeId,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            DateTimeOffset leaseExpiresAt,
            NodeState state = NodeState.Starting,
            IReadOnlyDictionary<string, string>? labels = null)
            : this(
                clusterName,
                nodeId,
                endpoints,
                Array.Empty<NodeActorHostDescriptor>(),
                Array.Empty<StartupActorDescriptor>(),
                leaseExpiresAt,
                state,
                labels)
        {
        }

        public NodeRegistration(
            string clusterName,
            NodeId nodeId,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeActorHostDescriptor> actorHosts,
            DateTimeOffset leaseExpiresAt,
            NodeState state = NodeState.Starting,
            IReadOnlyDictionary<string, string>? labels = null)
            : this(
                clusterName,
                nodeId,
                endpoints,
                actorHosts,
                Array.Empty<StartupActorDescriptor>(),
                leaseExpiresAt,
                state,
                labels)
        {
        }

        public NodeRegistration(
            string clusterName,
            NodeId nodeId,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeActorHostDescriptor> actorHosts,
            IReadOnlyList<StartupActorDescriptor> startupActors,
            DateTimeOffset leaseExpiresAt,
            NodeState state = NodeState.Starting,
            IReadOnlyDictionary<string, string>? labels = null)
        {
            if (string.IsNullOrWhiteSpace(clusterName))
            {
                throw new ArgumentException("Cluster name is required.", nameof(clusterName));
            }

            ClusterName = clusterName;
            NodeId = nodeId;
            Endpoints = CopyEndpoints(endpoints);
            ActorHosts = CopyActorHosts(actorHosts);
            StartupActors = CopyStartupActors(startupActors);
            Labels = CopyStringDictionary(labels, nameof(labels));
            State = state;
            LeaseExpiresAt = leaseExpiresAt;
        }

        public string ClusterName { get; }

        public NodeId NodeId { get; }

        public IReadOnlyDictionary<string, NodeEndpoint> Endpoints { get; }

        public IReadOnlyList<NodeActorHostDescriptor> ActorHosts { get; }

        public IReadOnlyList<StartupActorDescriptor> StartupActors { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        public NodeState State { get; }

        public DateTimeOffset LeaseExpiresAt { get; }

        private static IReadOnlyDictionary<string, NodeEndpoint> CopyEndpoints(
            IReadOnlyDictionary<string, NodeEndpoint> endpoints)
        {
            if (endpoints is null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (endpoints.Count == 0)
            {
                throw new ArgumentException("Node registration requires at least one endpoint.", nameof(endpoints));
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

        private static IReadOnlyList<StartupActorDescriptor> CopyStartupActors(
            IReadOnlyList<StartupActorDescriptor> startupActors)
        {
            if (startupActors is null)
            {
                throw new ArgumentNullException(nameof(startupActors));
            }

            var copy = new List<StartupActorDescriptor>(startupActors.Count);
            for (var i = 0; i < startupActors.Count; i++)
            {
                copy.Add(startupActors[i] ?? throw new ArgumentException(
                    "Startup actor descriptor cannot be null.",
                    nameof(startupActors)));
            }

            return new ReadOnlyCollection<StartupActorDescriptor>(copy);
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
