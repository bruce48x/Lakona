using Lakona.Game.Server.Internal.ActorKernel.Abstractions;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class TypedActorAdapter<TMessage> : IActor
{
    private readonly IActor<TMessage> actor;

    public TypedActorAdapter(IActor<TMessage> actor)
    {
        this.actor = actor;
    }

    public ValueTask OnMessage(ActorContextCore ctx, object message)
    {
        if (message is TMessage typedMessage)
        {
            return actor.OnMessage(new ActorContext<TMessage>(ctx), typedMessage);
        }

        throw new InvalidOperationException(
            $"Actor {ctx.Self.Id} expected message type {typeof(TMessage).FullName}, but received {message.GetType().FullName}.");
    }

    public ValueTask OnStarted(ActorContextCore ctx)
    {
        if (actor is IActorStarted<TMessage> started)
        {
            return started.OnStarted(new ActorContext<TMessage>(ctx));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStopping(ActorContextCore ctx)
    {
        if (actor is IActorStopping<TMessage> stopping)
        {
            return stopping.OnStopping(new ActorContext<TMessage>(ctx));
        }

        return ValueTask.CompletedTask;
    }
}
