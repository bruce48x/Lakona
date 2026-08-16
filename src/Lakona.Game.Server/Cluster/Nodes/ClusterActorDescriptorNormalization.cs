using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    internal static class ClusterActorDescriptorNormalization
    {
        private const int MaximumPublishedDescriptors = 256;
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal));

        internal static string RequireValue(
            string value,
            string message,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(message, parameterName);
            }

            return value;
        }

        internal static IReadOnlyDictionary<string, string> CopyActorHostMetadata(
            IReadOnlyDictionary<string, string>? metadata,
            string parameterName) =>
            CopyMetadata(metadata, "Actor host", parameterName);

        internal static IReadOnlyDictionary<string, string> CopyStartupActorMetadata(
            IReadOnlyDictionary<string, string>? metadata,
            string parameterName) =>
            CopyMetadata(metadata, "Startup actor", parameterName);

        internal static IReadOnlyList<NodeActorHostDescriptor> CopyActorHosts(
            IReadOnlyList<NodeActorHostDescriptor>? descriptors,
            string parameterName) =>
            CopyDescriptors(
                descriptors,
                static descriptor => descriptor.Actor,
                "Actor host",
                parameterName);

        internal static IReadOnlyList<StartupActorDescriptor> CopyStartupActors(
            IReadOnlyList<StartupActorDescriptor>? descriptors,
            string parameterName) =>
            CopyDescriptors(
                descriptors,
                static descriptor => descriptor.Actor,
                "Startup Actor",
                parameterName);

        private static IReadOnlyDictionary<string, string> CopyMetadata(
            IReadOnlyDictionary<string, string>? metadata,
            string descriptorName,
            string parameterName)
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
                    throw new ArgumentException(
                        $"{descriptorName} metadata keys cannot be empty.",
                        parameterName);
                }

                copy[pair.Key] = pair.Value ?? throw new ArgumentException(
                    $"{descriptorName} metadata values cannot be null.",
                    parameterName);
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }

        private static IReadOnlyList<TDescriptor> CopyDescriptors<TDescriptor>(
            IReadOnlyList<TDescriptor>? descriptors,
            Func<TDescriptor, string> actorSelector,
            string descriptorName,
            string parameterName)
            where TDescriptor : class
        {
            if (descriptors is null)
            {
                return Array.Empty<TDescriptor>();
            }

            if (descriptors.Count > MaximumPublishedDescriptors)
            {
                throw new ArgumentException(
                    $"A cluster member cannot publish more than {MaximumPublishedDescriptors} {descriptorName} descriptors.",
                    parameterName);
            }

            var copy = new List<TDescriptor>(descriptors.Count);
            var actors = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < descriptors.Count; i++)
            {
                var descriptor = descriptors[i] ?? throw new ArgumentException(
                    $"{descriptorName} descriptor cannot be null.",
                    parameterName);
                if (!actors.Add(actorSelector(descriptor)))
                {
                    throw new ArgumentException(
                        $"A cluster member cannot publish duplicate {descriptorName} names.",
                        parameterName);
                }

                copy.Add(descriptor);
            }

            copy.Sort((left, right) => string.Compare(
                actorSelector(left),
                actorSelector(right),
                StringComparison.Ordinal));
            return new ReadOnlyCollection<TDescriptor>(copy);
        }
    }
}
