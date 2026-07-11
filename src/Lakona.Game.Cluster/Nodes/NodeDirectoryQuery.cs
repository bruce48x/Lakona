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
            string? actorHostName = null,
            string? actorHostPolicyHash = null,
            NodeState? state = null,
            IReadOnlyDictionary<string, string>? labels = null,
            bool includeExpired = false,
            string? startupActorName = null,
            string? startupActorPolicyHash = null)
        {
            if (string.IsNullOrWhiteSpace(clusterName))
            {
                throw new ArgumentException("Cluster name is required.", nameof(clusterName));
            }

            if (actorHostName is not null && string.IsNullOrWhiteSpace(actorHostName))
            {
                throw new ArgumentException("Actor host name cannot be empty.", nameof(actorHostName));
            }

            if (actorHostPolicyHash is not null && string.IsNullOrWhiteSpace(actorHostPolicyHash))
            {
                throw new ArgumentException("Actor host policy hash cannot be empty.", nameof(actorHostPolicyHash));
            }

            if (actorHostPolicyHash is not null && actorHostName is null)
            {
                throw new ArgumentException("Actor host name is required when actor host policy hash is set.", nameof(actorHostName));
            }

            if (startupActorName is not null && string.IsNullOrWhiteSpace(startupActorName))
            {
                throw new ArgumentException("Startup actor name cannot be empty.", nameof(startupActorName));
            }

            if (startupActorPolicyHash is not null && string.IsNullOrWhiteSpace(startupActorPolicyHash))
            {
                throw new ArgumentException("Startup actor policy hash cannot be empty.", nameof(startupActorPolicyHash));
            }

            if (startupActorPolicyHash is not null && startupActorName is null)
            {
                throw new ArgumentException(
                    "Startup actor name is required when startup actor policy hash is set.",
                    nameof(startupActorName));
            }

            ClusterName = clusterName;
            ActorHostName = actorHostName;
            ActorHostPolicyHash = actorHostPolicyHash;
            StartupActorName = startupActorName;
            StartupActorPolicyHash = startupActorPolicyHash;
            State = state;
            Labels = CopyStringDictionary(labels, nameof(labels));
            IncludeExpired = includeExpired;
        }

        public string ClusterName { get; }

        public string? ActorHostName { get; }

        public string? ActorHostPolicyHash { get; }

        public string? StartupActorName { get; }

        public string? StartupActorPolicyHash { get; }

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
