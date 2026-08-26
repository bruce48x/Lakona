using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster
{
    public sealed class StartupActorDescriptor
    {
        public StartupActorDescriptor(
            string actor,
            string policyHash,
            string hotfixVersion,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            Actor = ClusterActorDescriptorNormalization.RequireValue(
                actor,
                "Startup actor name is required.",
                nameof(actor));
            PolicyHash = ClusterActorDescriptorNormalization.RequireValue(
                policyHash,
                "Startup actor policy hash is required.",
                nameof(policyHash));
            HotfixVersion = ClusterActorDescriptorNormalization.RequireValue(
                hotfixVersion,
                "Startup actor hotfix version is required.",
                nameof(hotfixVersion));
            Metadata = ClusterActorDescriptorNormalization.CopyStartupActorMetadata(
                metadata,
                nameof(metadata));
        }

        public string Actor { get; }

        public string PolicyHash { get; }

        public string HotfixVersion { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
