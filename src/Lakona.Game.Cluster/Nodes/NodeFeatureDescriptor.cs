using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class NodeFeatureDescriptor
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeFeatureDescriptor(string name, IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Feature name is required.", nameof(name));
            }

            Name = name;
            Metadata = CopyMetadata(metadata);
        }

        public string Name { get; }

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
                    throw new ArgumentException("Feature metadata keys cannot be empty.", nameof(metadata));
                }

                copy[pair.Key] = pair.Value ?? throw new ArgumentException(
                    "Feature metadata values cannot be null.",
                    nameof(metadata));
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }
    }
}
