using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Abstractions;

internal interface IActor
{
    ValueTask OnMessage(ActorContextCore ctx, object message);

    ValueTask OnStarted(ActorContextCore ctx);

    ValueTask OnStopping(ActorContextCore ctx);
}
