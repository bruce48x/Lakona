namespace Lakona.Game.Server.Actors;

public sealed record ActorActivationAcquireResult(
    ActorDirectoryRecord Record,
    bool Acquired);
