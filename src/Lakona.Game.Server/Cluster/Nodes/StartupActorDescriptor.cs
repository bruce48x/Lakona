using System;
using System.Collections.Generic;

namespace Lakona.Game.Cluster
{
    public sealed class StartupActorDescriptor
    {
        public StartupActorDescriptor(
            string actor,
            string policyHash,
            string buildTag,
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
            BuildTag = ClusterActorDescriptorNormalization.RequireValue(
                buildTag,
                "Startup actor build tag is required.",
                nameof(buildTag));
            Metadata = ClusterActorDescriptorNormalization.CopyStartupActorMetadata(
                metadata,
                nameof(metadata));
        }

        public string Actor { get; }

        public string PolicyHash { get; }

        public string BuildTag { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
