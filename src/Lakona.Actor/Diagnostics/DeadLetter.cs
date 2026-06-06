namespace Lakona.Actor;

public sealed record DeadLetter(ActorId Target, string MessageType, string Reason);
