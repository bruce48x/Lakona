using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorSystemDiagnosticsPublisher
{
    public event Action<DeadLetter>? DeadLetterPublished;

    public event Action<SlowMessage>? SlowMessageDetected;

    public event Action<ActorCallTimeout>? CallTimedOut;

    public event Action<ActorObserverError>? ObserverErrorPublished;

    public void PublishDeadLetter(ActorId target, object message, string reason)
    {
        LakonaActorDiagnostics.DeadLetterCounter.Add(1, new KeyValuePair<string, object?>(
            "reason",
            GetDeadLetterMetricReason(reason)));

        Action<DeadLetter>? handlers = DeadLetterPublished;

        if (handlers is null)
        {
            return;
        }

        string messageType = GetMessageType(message);
        DeadLetter deadLetter = new(target, messageType, reason);

        foreach (Action<DeadLetter> handler in handlers.GetInvocationList().Cast<Action<DeadLetter>>())
        {
            try
            {
                handler(deadLetter);
            }
            catch (Exception ex)
            {
                PublishObserverError(ActorObserverErrorSource.DeadLetterHandler, target, messageType, ex);
            }
        }
    }

    public void PublishSlowMessage(ActorId actorId, object message, TimeSpan elapsed)
    {
        Action<SlowMessage>? handlers = SlowMessageDetected;

        if (handlers is null)
        {
            return;
        }

        string messageType = GetMessageType(message);
        SlowMessage slowMessage = new(actorId, messageType, elapsed);

        foreach (Action<SlowMessage> handler in handlers.GetInvocationList().Cast<Action<SlowMessage>>())
        {
            try
            {
                handler(slowMessage);
            }
            catch (Exception ex)
            {
                PublishObserverError(ActorObserverErrorSource.SlowMessageHandler, actorId, messageType, ex);
            }
        }
    }

    public ActorCallTimeout CreateCallTimeout(
        ActorId? caller,
        ActorId target,
        object request,
        ActorCallOptions options,
        TimeSpan elapsed,
        ActorCallTimeoutReason reason,
        IReadOnlyList<ActorId> callChain)
    {
        ActorId[] snapshot = callChain.ToArray();
        return new ActorCallTimeout(
            caller,
            target,
            GetMessageType(request),
            options.QueueTimeout,
            options.ResponseTimeout,
            elapsed,
            reason,
            snapshot);
    }

    public TimeoutException CreateCallTimeoutException(ActorCallTimeout timeout, string message)
    {
        string chain = timeout.CallChain.Count == 0
            ? "<external>"
            : string.Join(" -> ", timeout.CallChain.Select(id => id.Value));

        return new TimeoutException(
            $"{message} Target={timeout.Target.Value}; Caller={timeout.Caller?.Value.ToString() ?? "<external>"}; " +
            $"Reason={timeout.Reason}; QueueTimeout={timeout.QueueTimeout}; ResponseTimeout={timeout.ResponseTimeout}; " +
            $"Elapsed={timeout.Elapsed}; Chain={chain}.");
    }

    public ActorCallTimeout PublishCallTimeout(
        ActorId? caller,
        ActorId target,
        object request,
        ActorCallOptions options,
        TimeSpan elapsed,
        ActorCallTimeoutReason reason,
        IReadOnlyList<ActorId> callChain)
    {
        ActorCallTimeout timeout = CreateCallTimeout(caller, target, request, options, elapsed, reason, callChain);
        PublishCallTimeout(timeout);
        return timeout;
    }

    public void PublishCallTimeout(ActorCallTimeout timeout)
    {
        LakonaActorDiagnostics.CallTimeoutCounter.Add(1, new KeyValuePair<string, object?>(
            "reason",
            timeout.Reason.ToString()));

        Action<ActorCallTimeout>? handlers = CallTimedOut;

        if (handlers is null)
        {
            return;
        }

        foreach (Action<ActorCallTimeout> handler in handlers.GetInvocationList().Cast<Action<ActorCallTimeout>>())
        {
            try
            {
                handler(timeout);
            }
            catch (Exception ex)
            {
                PublishObserverError(ActorObserverErrorSource.CallTimeoutHandler, timeout.Target, timeout.RequestType, ex);
            }
        }
    }

    public void PublishObserverError(
        ActorObserverErrorSource source,
        ActorId? actorId,
        string messageType,
        Exception exception)
    {
        Action<ActorObserverError>? handlers = ObserverErrorPublished;

        if (handlers is null)
        {
            return;
        }

        ActorObserverError observerError = new(source, actorId, messageType, exception);

        foreach (Action<ActorObserverError> handler in handlers.GetInvocationList().Cast<Action<ActorObserverError>>())
        {
            try
            {
                handler(observerError);
            }
            catch
            {
                // Observer error handlers are the last diagnostic boundary.
            }
        }
    }

    public static string GetMessageType(object message)
    {
        return message.GetType().FullName ?? message.GetType().Name;
    }

    private static string GetDeadLetterMetricReason(string reason)
    {
        return reason switch
        {
            "Actor does not exist." => "unavailable",
            "Actor is stopping." => "stopping",
            "Actor mailbox is completed." => "completed",
            "Actor mailbox is full." => "full",
            _ => "other"
        };
    }
}
