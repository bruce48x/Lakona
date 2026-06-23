namespace Lakona.Game.Server.Internal.ActorKernel;

internal interface IActor<TMessage>
{
    ValueTask OnMessage(ActorKernelContext<TMessage> ctx, TMessage message);
}

internal interface IActorStarted<TMessage>
{
    ValueTask OnStarted(ActorKernelContext<TMessage> ctx);
}

internal interface IActorStopping<TMessage>
{
    ValueTask OnStopping(ActorKernelContext<TMessage> ctx);
}
