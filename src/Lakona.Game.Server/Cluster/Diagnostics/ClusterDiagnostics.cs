using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Cluster
{
    public static class ClusterDiagnostics
    {
        public const string MeterName = LakonaGameServerTelemetry.ClusterMeterName;
        public const string ActivitySourceName = LakonaGameServerTelemetry.ClusterActivitySourceName;
        private static readonly string? InstrumentationVersion =
            typeof(ClusterDiagnostics).Assembly.GetName().Version?.ToString();
        public static readonly Meter Meter = new(MeterName, InstrumentationVersion);
        public static readonly ActivitySource ActivitySource = new(ActivitySourceName, InstrumentationVersion);
        private static readonly Counter<long> MembershipRequestCounter = Meter.CreateCounter<long>(
            "lakona.game.cluster.membership.request");
        private static readonly Histogram<double> MembershipRequestDuration = Meter.CreateHistogram<double>(
            "lakona.game.cluster.membership.request.duration",
            unit: "s");
        private static readonly Counter<long> AuthorityTransitionCounter = Meter.CreateCounter<long>(
            "lakona.game.cluster.authority.transition");
        private static readonly Histogram<double> ActorLocationRecoveryDuration = Meter.CreateHistogram<double>(
            "lakona.game.cluster.actor_location.recovery.duration",
            unit: "s");
        private static readonly Counter<long> ActorLocationFailureCounter = Meter.CreateCounter<long>(
            "lakona.game.cluster.actor_location.failure");
        private static readonly Counter<long> ActorRequestProofFailureCounter = Meter.CreateCounter<long>(
            "lakona.game.cluster.actor_request.proof_failure");

        internal static Activity? StartActivity(string name) =>
            ActivitySource.StartActivity(name, ActivityKind.Internal);

        internal static void RecordMembershipRequest(string outcome, TimeSpan elapsed)
        {
            var tag = new KeyValuePair<string, object?>("lakona.game.cluster.outcome", outcome);
            MembershipRequestCounter.Add(1, tag);
            MembershipRequestDuration.Record(elapsed.TotalSeconds, tag);
        }

        internal static void RecordAuthorityTransition(string state) =>
            AuthorityTransitionCounter.Add(
                1,
                new KeyValuePair<string, object?>("lakona.game.cluster.authority.state", state));

        internal static void RecordActorLocationRecovery(string outcome, TimeSpan elapsed) =>
            ActorLocationRecoveryDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("lakona.game.cluster.outcome", outcome));

        internal static void RecordActorLocationFailure(ActorLocationFailureReason reason) =>
            ActorLocationFailureCounter.Add(
                1,
                new KeyValuePair<string, object?>(
                    "lakona.game.cluster.reason",
                    reason switch
                    {
                        ActorLocationFailureReason.Unavailable => "unavailable",
                        ActorLocationFailureReason.Conflict => "conflict",
                        ActorLocationFailureReason.Capacity => "capacity",
                        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
                    }));

        internal static void RecordActorRequestProofFailure(ActorRequestProofFailureReason reason) =>
            ActorRequestProofFailureCounter.Add(
                1,
                new KeyValuePair<string, object?>(
                    "lakona.game.cluster.reason",
                    reason switch
                    {
                        ActorRequestProofFailureReason.ClusterIncarnation => "cluster_incarnation",
                        ActorRequestProofFailureReason.LocalNode => "local_node",
                        ActorRequestProofFailureReason.TargetNode => "target_node",
                        ActorRequestProofFailureReason.NodeIncarnation => "node_incarnation",
                        ActorRequestProofFailureReason.MembershipView => "membership_view",
                        ActorRequestProofFailureReason.DirectoryUnavailable => "directory_unavailable",
                        ActorRequestProofFailureReason.Activation => "activation",
                        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
                    }));
    }

    internal enum ActorLocationFailureReason
    {
        Unavailable,
        Conflict,
        Capacity
    }

    internal enum ActorRequestProofFailureReason
    {
        None,
        ClusterIncarnation,
        LocalNode,
        TargetNode,
        NodeIncarnation,
        MembershipView,
        DirectoryUnavailable,
        Activation
    }
}
