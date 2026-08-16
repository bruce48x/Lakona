using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster
{
    public sealed class NodeActorHostDescriptor
    {
        public NodeActorHostDescriptor(
            string actor,
            string policyHash,
            string buildTag,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            Actor = ClusterActorDescriptorNormalization.RequireValue(
                actor,
                "Actor host name is required.",
                nameof(actor));
            PolicyHash = ClusterActorDescriptorNormalization.RequireValue(
                policyHash,
                "Actor host policy hash is required.",
                nameof(policyHash));
            BuildTag = ClusterActorDescriptorNormalization.RequireValue(
                buildTag,
                "Actor host build tag is required.",
                nameof(buildTag));
            Metadata = ClusterActorDescriptorNormalization.CopyActorHostMetadata(
                metadata,
                nameof(metadata));
        }

        public string Actor { get; }

        public string PolicyHash { get; }

        public string BuildTag { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
