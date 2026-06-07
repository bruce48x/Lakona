namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed record DeadLetter(ActorId Target, string MessageType, string Reason);
