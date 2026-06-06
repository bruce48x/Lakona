namespace Lakona.Actor;

public sealed record SlowMessage(ActorId ActorId, string MessageType, TimeSpan Elapsed);
