using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailboxCore = Lakona.Actor.Mailbox.Mailbox;

namespace Lakona.Actor;

public static class LakonaActorDiagnostics
{
    public const string ActivitySourceName = "Lakona.Actor";

    public const string MeterName = "Lakona.Actor";

    public static readonly ActivitySource ActivitySource = new(
        ActivitySourceName,
        typeof(LakonaActorDiagnostics).Assembly.GetName().Version?.ToString());

    internal static readonly Meter Meter = new(
        MeterName,
        typeof(LakonaActorDiagnostics).Assembly.GetName().Version?.ToString());

    internal static readonly Counter<long> MessageAcceptedCounter = Meter.CreateCounter<long>(
        "lakona-actor.message.accepted");

    internal static readonly Counter<long> MessageRejectedCounter = Meter.CreateCounter<long>(
        "lakona-actor.message.rejected");

    internal static readonly Counter<long> MessageProcessedCounter = Meter.CreateCounter<long>(
        "lakona-actor.message.processed");

    internal static readonly Counter<long> CallStartedCounter = Meter.CreateCounter<long>(
        "lakona-actor.call.started");

    internal static readonly Counter<long> CallTimeoutCounter = Meter.CreateCounter<long>(
        "lakona-actor.call.timeout");

    internal static readonly Counter<long> DeadLetterCounter = Meter.CreateCounter<long>(
        "lakona-actor.deadletter.published");

    private static readonly ObservableGauge<long> MailboxQueueLengthGauge = Meter.CreateObservableGauge(
        "lakona-actor.mailbox.queue.length",
        static () => MailboxCore.GetTotalQueuedCount());
}
