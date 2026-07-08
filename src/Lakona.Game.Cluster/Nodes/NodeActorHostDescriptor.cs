using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class NodeActorHostDescriptor
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeActorHostDescriptor(
            string actor,
            string policyHash,
            string buildTag,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(actor))
            {
                throw new ArgumentException("Actor host name is required.", nameof(actor));
            }

            if (string.IsNullOrWhiteSpace(policyHash))
            {
                throw new ArgumentException("Actor host policy hash is required.", nameof(policyHash));
            }

            if (string.IsNullOrWhiteSpace(buildTag))
            {
                throw new ArgumentException("Actor host build tag is required.", nameof(buildTag));
            }

            Actor = actor;
            PolicyHash = policyHash;
            BuildTag = buildTag;
            Metadata = CopyMetadata(metadata);
        }

        public string Actor { get; }

        public string PolicyHash { get; }

        public string BuildTag { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        private static IReadOnlyDictionary<string, string> CopyMetadata(
            IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata is null)
            {
                return EmptyMetadata;
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in metadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException("Actor host metadata keys cannot be empty.", nameof(metadata));
                }

                copy[pair.Key] = pair.Value ?? throw new ArgumentException(
                    "Actor host metadata values cannot be null.",
                    nameof(metadata));
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }
    }
}
