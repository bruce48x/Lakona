using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeDiscoveryQuery
    {
        public ClusterNodeDiscoveryQuery(
            NodeState state = NodeState.Ready,
            string? actorHostName = null,
            string? actorHostPolicyHash = null,
            string? startupActorName = null,
            string? startupActorPolicyHash = null,
            IReadOnlyDictionary<string, string>? labels = null)
        {
            State = state;
            ActorHostName = Normalize(actorHostName);
            ActorHostPolicyHash = Normalize(actorHostPolicyHash);
            StartupActorName = Normalize(startupActorName);
            StartupActorPolicyHash = Normalize(startupActorPolicyHash);
            Labels = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(
                    labels ?? new Dictionary<string, string>(),
                    StringComparer.Ordinal));

            if (ActorHostPolicyHash is not null && ActorHostName is null)
            {
                throw new ArgumentException(
                    "An actor host policy hash requires an actor host name.",
                    nameof(actorHostPolicyHash));
            }

            if (StartupActorPolicyHash is not null && StartupActorName is null)
            {
                throw new ArgumentException(
                    "A startup Actor policy hash requires a startup Actor name.",
                    nameof(startupActorPolicyHash));
            }
        }

        public NodeState State { get; }

        public string? ActorHostName { get; }

        public string? ActorHostPolicyHash { get; }

        public string? StartupActorName { get; }

        public string? StartupActorPolicyHash { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }

        public bool Matches(ClusterNodeDescriptor node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (node.State != State || !MatchesLabels(node.Labels))
            {
                return false;
            }

            if (ActorHostName is not null
                && !node.ActorHosts.Any(host =>
                    string.Equals(host.Actor, ActorHostName, StringComparison.Ordinal)
                    && (ActorHostPolicyHash is null
                        || string.Equals(
                            host.PolicyHash,
                            ActorHostPolicyHash,
                            StringComparison.Ordinal))))
            {
                return false;
            }

            return StartupActorName is null
                || node.StartupActors.Any(actor =>
                    string.Equals(actor.Actor, StartupActorName, StringComparison.Ordinal)
                    && (StartupActorPolicyHash is null
                        || string.Equals(
                            actor.PolicyHash,
                            StartupActorPolicyHash,
                            StringComparison.Ordinal)));
        }

        private bool MatchesLabels(IReadOnlyDictionary<string, string> labels)
        {
            foreach (var pair in Labels)
            {
                if (!labels.TryGetValue(pair.Key, out var value)
                    || !string.Equals(value, pair.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
