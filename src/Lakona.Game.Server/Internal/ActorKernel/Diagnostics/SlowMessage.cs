namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed record SlowMessage(ActorId ActorId, string MessageType, TimeSpan Elapsed);
