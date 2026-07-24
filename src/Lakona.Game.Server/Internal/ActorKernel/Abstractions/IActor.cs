namespace Lakona.Game.Server.Internal.ActorKernel;

internal interface IActor<TMessage>
{
    ValueTask OnMessage(ActorKernelContext<TMessage> ctx, TMessage message);
}
