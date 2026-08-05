using System.Text.Json;
using Lakona.Game.Cluster;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

internal enum ActorActivationReplicaFailurePhase
{
    AuthoritativeRead = 0,
    ReplicaRepair = 1,
    QuorumCommit = 2,
    AdditionalPropagation = 3,
}

internal sealed class ActorActivationReplicaDiagnostics
{
    private static readonly TimeSpan ReportingWindow = TimeSpan.FromSeconds(10);
    private static readonly EventId FailureEvent = new(4101, "ActorActivationReplicaFailure");
    private readonly ILogger<ReplicatedActorActivationDirectory>? logger;
    private readonly TimeProvider timeProvider;
    private readonly FailureBucket[] buckets =
        Enumerable.Range(0, 4).Select(static _ => new FailureBucket()).ToArray();

    public ActorActivationReplicaDiagnostics(
        ILogger<ReplicatedActorActivationDirectory>? logger,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public void Report(
        ActorActivationReplicaFailurePhase phase,
        ClusterMember target,
        MembershipViewId membershipView,
        string result,
        Exception? exception)
    {
        if (logger is null || !logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        var bucket = buckets[(int)phase];
        long suppressedCount;
        var now = timeProvider.GetUtcNow();
        lock (bucket.Gate)
        {
            if (now >= bucket.WindowStartedUtc && now < bucket.NextReportUtc)
            {
                bucket.SuppressedCount++;
                return;
            }

            suppressedCount = bucket.SuppressedCount;
            bucket.SuppressedCount = 0;
            bucket.WindowStartedUtc = now;
            bucket.NextReportUtc = now.Add(ReportingWindow);
        }

        logger.LogWarning(
            FailureEvent,
            exception,
            "Actor activation replica {Phase} failed for target {TargetNode}@{TargetNodeIncarnation} in membership view {MembershipView} with result {Result}, exception category {ExceptionCategory}, exception type {ExceptionType}, and {SuppressedCount} suppressed failures.",
            PhaseName(phase),
            target.Reference.Node.Value,
            target.Reference.Incarnation.Value,
            membershipView.Value,
            result,
            ExceptionCategory(exception),
            exception?.GetType().Name ?? "none",
            suppressedCount);
    }

    private static string PhaseName(ActorActivationReplicaFailurePhase phase) => phase switch
    {
        ActorActivationReplicaFailurePhase.AuthoritativeRead => "authoritative-read",
        ActorActivationReplicaFailurePhase.ReplicaRepair => "replica-repair",
        ActorActivationReplicaFailurePhase.QuorumCommit => "quorum-commit",
        ActorActivationReplicaFailurePhase.AdditionalPropagation => "additional-propagation",
        _ => "unknown",
    };

    private static string ExceptionCategory(Exception? exception) => exception switch
    {
        null => "none",
        TimeoutException => "timeout",
        OperationCanceledException => "timeout",
        JsonException => "protocol",
        ActorDirectoryUnavailableException => "unavailable",
        _ => "unexpected",
    };

    private sealed class FailureBucket
    {
        public object Gate { get; } = new();

        public DateTimeOffset WindowStartedUtc { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset NextReportUtc { get; set; } = DateTimeOffset.MinValue;

        public long SuppressedCount { get; set; }
    }
}
