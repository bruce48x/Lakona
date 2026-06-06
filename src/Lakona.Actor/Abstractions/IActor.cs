namespace Lakona.Actor;

public interface IActor<TMessage>
{
    ValueTask OnMessage(ActorContext<TMessage> ctx, TMessage message);
}

public interface IActorStarted<TMessage>
{
    ValueTask OnStarted(ActorContext<TMessage> ctx);
}

public interface IActorStopping<TMessage>
{
    ValueTask OnStopping(ActorContext<TMessage> ctx);
}
