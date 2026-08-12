using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.Actors.Internal;

internal static class LakonaActorDiagnostics
{
    public const string ActivitySourceName = LakonaGameServerTelemetry.ActorActivitySourceName;

    public const string MeterName = LakonaGameServerTelemetry.ActorMeterName;

    public static readonly ActivitySource ActivitySource = new(
        ActivitySourceName,
        typeof(LakonaActorDiagnostics).Assembly.GetName().Version?.ToString());

    internal static readonly Meter Meter = new(
        MeterName,
        typeof(LakonaActorDiagnostics).Assembly.GetName().Version?.ToString());

    internal static readonly Counter<long> MessageAcceptedCounter = Meter.CreateCounter<long>(
        "lakona.game.actor.message.accepted",
        unit: "{message}",
        description: "Actor messages accepted by a mailbox.");

    internal static readonly Counter<long> MessageRejectedCounter = Meter.CreateCounter<long>(
        "lakona.game.actor.message.rejected",
        unit: "{message}",
        description: "Actor messages rejected before dispatch.");

    internal static readonly Counter<long> MessageProcessedCounter = Meter.CreateCounter<long>(
        "lakona.game.actor.message.processed",
        unit: "{message}",
        description: "Actor messages whose dispatch completed.");

    internal static readonly Counter<long> CallStartedCounter = Meter.CreateCounter<long>(
        "lakona.game.actor.call.started",
        unit: "{call}",
        description: "Actor calls started.");

    internal static readonly Counter<long> CallTimeoutCounter = Meter.CreateCounter<long>(
        "lakona.game.actor.call.timeout",
        unit: "{call}",
        description: "Actor calls that timed out.");

    internal static readonly Counter<long> DeadLetterCounter = Meter.CreateCounter<long>(
        "lakona.game.actor.deadletter.published",
        unit: "{message}",
        description: "Actor messages published as dead letters.");

    private static readonly ObservableGauge<long> MailboxQueueLengthGauge = Meter.CreateObservableGauge(
        "lakona.game.actor.mailbox.queue.length",
        static () => ActorMailbox.GetTotalQueuedCount(),
        unit: "{message}",
        description: "Messages currently queued across local actor mailboxes.");
}
