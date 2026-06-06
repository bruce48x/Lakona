using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class NodeServiceDescriptor
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeServiceDescriptor(
            string kind,
            string? name = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Node service kind is required.", nameof(kind));
            }

            if (name is not null && string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Node service name cannot be empty.", nameof(name));
            }

            Kind = kind;
            Name = name ?? kind;
            Metadata = CopyStringDictionary(metadata, nameof(metadata));
        }

        public string Kind { get; }

        public string Name { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        private static IReadOnlyDictionary<string, string> CopyStringDictionary(
            IReadOnlyDictionary<string, string>? source,
            string parameterName)
        {
            if (source is null)
            {
                return EmptyMetadata;
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
