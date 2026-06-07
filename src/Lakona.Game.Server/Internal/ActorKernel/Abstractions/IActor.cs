namespace Lakona.Game.Server.Internal.ActorKernel;

internal interface IActor<TMessage>
{
    ValueTask OnMessage(ActorContext<TMessage> ctx, TMessage message);
}

internal interface IActorStarted<TMessage>
{
    ValueTask OnStarted(ActorContext<TMessage> ctx);
}

internal interface IActorStopping<TMessage>
{
    ValueTask OnStopping(ActorContext<TMessage> ctx);
}
