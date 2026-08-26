using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster
{
    public sealed class NodeActorHostDescriptor
    {
        public NodeActorHostDescriptor(
            string actor,
            string policyHash,
            string hotfixVersion,
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
            HotfixVersion = ClusterActorDescriptorNormalization.RequireValue(
                hotfixVersion,
                "Actor host hotfix version is required.",
                nameof(hotfixVersion));
            Metadata = ClusterActorDescriptorNormalization.CopyActorHostMetadata(
                metadata,
                nameof(metadata));
        }

        public string Actor { get; }

        public string PolicyHash { get; }

        public string HotfixVersion { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
