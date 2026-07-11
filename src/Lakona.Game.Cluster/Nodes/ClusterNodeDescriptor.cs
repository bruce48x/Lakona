using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeDescriptor
    {
        public ClusterNodeDescriptor(
            NodeId node,
            NodeState state,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeActorHostDescriptor>? actorHosts = null,
            IReadOnlyDictionary<string, string>? labels = null)
            : this(node, state, endpoints, actorHosts, Array.Empty<StartupActorDescriptor>(), labels)
        {
        }

        public ClusterNodeDescriptor(
            NodeId node,
            NodeState state,
            IReadOnlyDictionary<string, NodeEndpoint> endpoints,
            IReadOnlyList<NodeActorHostDescriptor>? actorHosts,
            IReadOnlyList<StartupActorDescriptor> startupActors,
            IReadOnlyDictionary<string, string>? labels = null)
        {
            Node = node;
            State = state;
            Endpoints = CopyEndpoints(endpoints);
            ActorHosts = CopyActorHosts(actorHosts ?? Array.Empty<NodeActorHostDescriptor>());
            StartupActors = CopyStartupActors(startupActors);
            Labels = CopyLabels(labels);
        }

        public NodeId Node { get; }

        public NodeState State { get; }

        public IReadOnlyDictionary<string, NodeEndpoint> Endpoints { get; }

        public IReadOnlyList<NodeActorHostDescriptor> ActorHosts { get; }

        public IReadOnlyList<StartupActorDescriptor> StartupActors { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        public static ClusterNodeDescriptor FromRecord(NodeRecord record)
        {
            if (record is null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            return new ClusterNodeDescriptor(
                record.NodeId,
                record.State,
                record.Endpoints,
                record.ActorHosts,
                record.StartupActors,
                record.Labels);
        }

        private static IReadOnlyDictionary<string, NodeEndpoint> CopyEndpoints(
            IReadOnlyDictionary<string, NodeEndpoint> endpoints)
        {
            if (endpoints is null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            return new ReadOnlyDictionary<string, NodeEndpoint>(
                new Dictionary<string, NodeEndpoint>(endpoints, StringComparer.Ordinal));
        }

        private static IReadOnlyList<NodeActorHostDescriptor> CopyActorHosts(
            IReadOnlyList<NodeActorHostDescriptor> actorHosts)
        {
            return new ReadOnlyCollection<NodeActorHostDescriptor>(new List<NodeActorHostDescriptor>(actorHosts));
        }

        private static IReadOnlyList<StartupActorDescriptor> CopyStartupActors(
            IReadOnlyList<StartupActorDescriptor> startupActors)
        {
            if (startupActors is null)
            {
                throw new ArgumentNullException(nameof(startupActors));
            }

            return new ReadOnlyCollection<StartupActorDescriptor>(new List<StartupActorDescriptor>(startupActors));
        }

        private static IReadOnlyDictionary<string, string> CopyLabels(
            IReadOnlyDictionary<string, string>? labels)
        {
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(labels ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        }
    }
}
