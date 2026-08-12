namespace Lakona.Game.Server.Actors.Internal;

internal sealed class ActorRuntimeDiagnosticsPublisher
{
    private readonly ActorRuntimeOptions _options;
    internal ActorRuntimeDiagnosticsPublisher(ActorRuntimeOptions options)
    {
        _options = options;
    }

    internal void PublishDeadLetter(ActorId target, ActorWorkItem work, string reason)
    {
        LakonaActorDiagnostics.DeadLetterCounter.Add(1, new KeyValuePair<string, object?>(
            "reason",
            GetDeadLetterMetricReason(reason)));

        ActorDeadLetterDiagnostic diagnostic = new(target, work.MessageType, reason);
        if (_options.DeadLetterHandler is { } handler)
        {
            TryInvoke(() => handler(diagnostic));
        }
    }

    internal void PublishSlowMessage(ActorId actorId, ActorWorkItem work, TimeSpan elapsed)
    {
        ActorSlowMessageDiagnostic diagnostic = new(actorId, work.MessageType, elapsed);
        if (_options.SlowMessageHandler is { } handler)
        {
            TryInvoke(() => handler(diagnostic));
        }
    }

    internal TimeoutException PublishCallTimeout(
        ActorId? caller,
        ActorId target,
        ActorWorkItem work,
        TimeSpan queueTimeout,
        TimeSpan responseTimeout,
        TimeSpan elapsed,
        ActorCallTimeoutReason reason,
        IReadOnlyList<ActorId> callChain,
        string message)
    {
        LakonaActorDiagnostics.CallTimeoutCounter.Add(1, new KeyValuePair<string, object?>(
            "reason",
            reason.ToString()));

        ActorId[] chainSnapshot = callChain.ToArray();
        TimeSpan timeout = reason == ActorCallTimeoutReason.QueueTimeout
            ? queueTimeout
            : responseTimeout;
        ActorCallTimeoutDiagnostic diagnostic = new(
            caller,
            target,
            work.MessageType,
            timeout,
            reason,
            chainSnapshot);

        if (_options.CallTimeoutHandler is { } handler)
        {
            TryInvoke(() => handler(diagnostic));
        }

        string chain = chainSnapshot.Length == 0
            ? "<external>"
            : string.Join(" -> ", chainSnapshot.Select(static id => id.Value));
        return new TimeoutException(
            $"{message} Target={target.Value}; Caller={caller?.Value ?? "<external>"}; " +
            $"Reason={reason}; QueueTimeout={queueTimeout}; ResponseTimeout={responseTimeout}; " +
            $"Elapsed={elapsed}; Chain={chain}.");
    }

    private static void TryInvoke(Action callback)
    {
        try
        {
            callback();
        }
        catch
        {
            // Diagnostics cannot affect actor dispatch.
        }
    }

    private static string GetDeadLetterMetricReason(string reason)
    {
        return reason switch
        {
            "Actor is stopping." => "stopping",
            "Actor mailbox is completed." => "completed",
            "Actor mailbox is full." => "full",
            _ => "other"
        };
    }
}
