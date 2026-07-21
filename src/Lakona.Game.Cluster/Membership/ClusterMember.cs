using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterMember
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal));
        private static readonly IReadOnlyList<NodeActorHostDescriptor> EmptyActorHosts =
            new ReadOnlyCollection<NodeActorHostDescriptor>(new List<NodeActorHostDescriptor>());
        private static readonly IReadOnlyList<StartupActorDescriptor> EmptyStartupActors =
            new ReadOnlyCollection<StartupActorDescriptor>(new List<StartupActorDescriptor>());

        public ClusterMember(
            NodeReference reference,
            ClusterMemberState state,
            NodeEndpoint clusterEndpoint,
            bool isVoter)
            : this(reference, state, clusterEndpoint, isVoter, null, null, null)
        {
        }

        public ClusterMember(
            NodeReference reference,
            ClusterMemberState state,
            NodeEndpoint clusterEndpoint,
            bool isVoter,
            IReadOnlyDictionary<string, string>? labels)
            : this(reference, state, clusterEndpoint, isVoter, labels, null, null)
        {
        }

        public ClusterMember(
            NodeReference reference,
            ClusterMemberState state,
            NodeEndpoint clusterEndpoint,
            bool isVoter,
            IReadOnlyDictionary<string, string>? labels,
            IReadOnlyList<NodeActorHostDescriptor>? actorHosts,
            IReadOnlyList<StartupActorDescriptor>? startupActors)
        {
            Reference = reference ?? throw new ArgumentNullException(nameof(reference));
            State = state;
            ClusterEndpoint = clusterEndpoint ?? throw new ArgumentNullException(nameof(clusterEndpoint));
            IsVoter = isVoter;
            Labels = CopyLabels(labels);
            ActorHosts = CopyActorHosts(actorHosts);
            StartupActors = CopyStartupActors(startupActors);
        }

        public NodeReference Reference { get; }

        public ClusterMemberState State { get; }

        public NodeEndpoint ClusterEndpoint { get; }

        public bool IsVoter { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        public IReadOnlyList<NodeActorHostDescriptor> ActorHosts { get; }

        public IReadOnlyList<StartupActorDescriptor> StartupActors { get; }

        private static IReadOnlyDictionary<string, string> CopyLabels(
            IReadOnlyDictionary<string, string>? labels)
        {
            if (labels is null)
            {
                return EmptyLabels;
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in labels)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException("Cluster member label names cannot be empty.", nameof(labels));
                }

                copy[pair.Key] = pair.Value ?? throw new ArgumentException(
                    "Cluster member label values cannot be null.",
                    nameof(labels));
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }

        private static IReadOnlyList<NodeActorHostDescriptor> CopyActorHosts(
            IReadOnlyList<NodeActorHostDescriptor>? actorHosts)
        {
            if (actorHosts is null)
            {
                return EmptyActorHosts;
            }

            if (actorHosts.Count > 256)
            {
                throw new ArgumentException(
                    "A cluster member cannot publish more than 256 Actor host descriptors.",
                    nameof(actorHosts));
            }

            var copy = new List<NodeActorHostDescriptor>(actorHosts.Count);
            var actors = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < actorHosts.Count; i++)
            {
                var descriptor = actorHosts[i] ?? throw new ArgumentException(
                    "Actor host descriptor cannot be null.",
                    nameof(actorHosts));
                if (!actors.Add(descriptor.Actor))
                {
                    throw new ArgumentException(
                        "A cluster member cannot publish duplicate Actor host names.",
                        nameof(actorHosts));
                }

                copy.Add(descriptor);
            }

            copy.Sort((left, right) => string.Compare(
                left.Actor,
                right.Actor,
                StringComparison.Ordinal));
            return new ReadOnlyCollection<NodeActorHostDescriptor>(copy);
        }

        private static IReadOnlyList<StartupActorDescriptor> CopyStartupActors(
            IReadOnlyList<StartupActorDescriptor>? startupActors)
        {
            if (startupActors is null)
            {
                return EmptyStartupActors;
            }

            if (startupActors.Count > 256)
            {
                throw new ArgumentException(
                    "A cluster member cannot publish more than 256 Startup Actor descriptors.",
                    nameof(startupActors));
            }

            var copy = new List<StartupActorDescriptor>(startupActors.Count);
            var actors = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < startupActors.Count; i++)
            {
                var descriptor = startupActors[i] ?? throw new ArgumentException(
                    "Startup Actor descriptor cannot be null.",
                    nameof(startupActors));
                if (!actors.Add(descriptor.Actor))
                {
                    throw new ArgumentException(
                        "A cluster member cannot publish duplicate Startup Actor names.",
                        nameof(startupActors));
                }

                copy.Add(descriptor);
            }

            copy.Sort((left, right) => string.Compare(
                left.Actor,
                right.Actor,
                StringComparison.Ordinal));
            return new ReadOnlyCollection<StartupActorDescriptor>(copy);
        }
    }
}
