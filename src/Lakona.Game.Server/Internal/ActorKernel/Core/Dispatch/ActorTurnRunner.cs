using System.Diagnostics;
using Lakona.Game.Server.Internal.ActorKernel.Abstractions;
using Lakona.Game.Server.Internal.ActorKernel.Lifecycle;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorTurnRunner
{
    private readonly ActorSystem system;
    private readonly ActorCell cell;
    private readonly ActorRef self;
    private readonly IActor actor;
    private readonly TimeSpan? slowMessageThreshold;

    internal ActorTurnRunner(
        ActorSystem system,
        ActorCell cell,
        ActorRef self,
        IActor actor,
        TimeSpan? slowMessageThreshold)
    {
        this.system = system;
        this.cell = cell;
        this.self = self;
        this.actor = actor;
        this.slowMessageThreshold = slowMessageThreshold;
    }

    internal async ValueTask Dispatch(Envelope envelope)
    {
        ActorContextCore context = new(self, cell, envelope);
        ActorCallContext? previousCallContext = system.CurrentCallContext;
        IReadOnlyList<ActorId> callChain = AppendCallChain(envelope.CallChain, self.Id);
        ActorCallContext currentCallContext = new(self.Id, callChain);
        long startedAt = slowMessageThreshold is null ? 0 : Stopwatch.GetTimestamp();
        string messageType = envelope.Message.GetType().FullName ?? envelope.Message.GetType().Name;
        IActorMessageInterceptor? interceptor = system.MessageInterceptor;

        using Activity? activity = StartDispatchActivity(envelope);

        activity?.SetTag("lakona-actor.actor.id", self.Id.Value);
        activity?.SetTag("lakona-actor.message.type", messageType);
        activity?.SetTag("lakona-actor.message.kind", envelope.Response is null ? "send" : "call");
        activity?.SetTag("lakona-actor.call.chain", string.Join(">", callChain.Select(id => id.Value)));

        Exception? error = null;

        try
        {
            if (interceptor is not null)
            {
                await RunBeforeInterceptor(interceptor, envelope, messageType).ConfigureAwait(false);
            }

            system.CurrentCallContext = currentCallContext;

            if (envelope.Message is ActorLifecycleMessage.Stopping)
            {
                await actor.OnStopping(context).ConfigureAwait(false);
                envelope.Response?.TrySetResult(null);
            }
            else
            {
                await actor.OnMessage(context, envelope.Message).ConfigureAwait(false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            error = ex;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
        }
        finally
        {
            currentCallContext.Deactivate();
            system.CurrentCallContext = previousCallContext;
            LakonaActorDiagnostics.MessageProcessedCounter.Add(1, CreateMessageKindTag(envelope));

            if (slowMessageThreshold is not null)
            {
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

                if (elapsed >= slowMessageThreshold.Value)
                {
                    activity?.AddEvent(new ActivityEvent(
                        "Lakona.Actor.Actor.SlowMessage",
                        tags: new ActivityTagsCollection
                        {
                            ["lakona-actor.slow_message.elapsed_ms"] = elapsed.TotalMilliseconds
                        }));
                    activity?.SetTag("lakona-actor.slow_message", true);
                    activity?.SetTag("lakona-actor.slow_message.elapsed_ms", elapsed.TotalMilliseconds);
                    system.Diagnostics.PublishSlowMessage(self.Id, envelope.Message, elapsed);
                }
            }

            if (interceptor is not null)
            {
                await RunAfterInterceptor(interceptor, envelope, messageType, error).ConfigureAwait(false);
            }

            if (error is not null)
            {
                envelope.Response?.TrySetException(error);
            }
        }
    }

    private async ValueTask RunBeforeInterceptor(
        IActorMessageInterceptor interceptor,
        Envelope envelope,
        string messageType)
    {
        try
        {
            await interceptor.OnBeforeMessage(self.Id, envelope.Message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            system.Diagnostics.PublishObserverError(
                ActorObserverErrorSource.MessageInterceptorBefore,
                self.Id,
                messageType,
                ex);
        }
    }

    private async ValueTask RunAfterInterceptor(
        IActorMessageInterceptor interceptor,
        Envelope envelope,
        string messageType,
        Exception? error)
    {
        try
        {
            await interceptor.OnAfterMessage(self.Id, envelope.Message, error, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            system.Diagnostics.PublishObserverError(
                ActorObserverErrorSource.MessageInterceptorAfter,
                self.Id,
                messageType,
                ex);
        }
    }

    private static IReadOnlyList<ActorId> AppendCallChain(IReadOnlyList<ActorId> callChain, ActorId actorId)
    {
        ActorId[] next = new ActorId[callChain.Count + 1];

        for (int i = 0; i < callChain.Count; i++)
        {
            next[i] = callChain[i];
        }

        next[^1] = actorId;
        return next;
    }

    private static KeyValuePair<string, object?> CreateMessageKindTag(Envelope envelope)
    {
        return new KeyValuePair<string, object?>("kind", envelope.Response is null ? "send" : "call");
    }

    private static Activity? StartDispatchActivity(Envelope envelope)
    {
        if (envelope.ParentActivityContext.TraceId != default)
        {
            return LakonaActorDiagnostics.ActivitySource.StartActivity(
                "Lakona.Actor.Actor.Dispatch",
                ActivityKind.Internal,
                envelope.ParentActivityContext);
        }

        return LakonaActorDiagnostics.ActivitySource.StartActivity(
            "Lakona.Actor.Actor.Dispatch",
            ActivityKind.Internal);
    }
}
