namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixLocalActorDeclaration(
    Type ActorType,
    string ActorId);
