namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorStartupInstance(
    Type ActorType,
    object ActorId);
