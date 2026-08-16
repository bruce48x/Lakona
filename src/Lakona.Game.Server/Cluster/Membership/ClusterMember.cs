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
            ActorHosts = ClusterActorDescriptorNormalization.CopyActorHosts(
                actorHosts,
                nameof(actorHosts));
            StartupActors = ClusterActorDescriptorNormalization.CopyStartupActors(
                startupActors,
                nameof(startupActors));
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
    }
}
