namespace Lakona.Game.Server.Hotfix.Abstractions;

public enum HotfixActorTickMode
{
    FixedActor = 0,
    ActiveActors = 1
}

public sealed record HotfixActorTickDeclaration(
    HotfixActorTickMode Mode,
    Type ActorType,
    string ActorId,
    string MethodName,
    TimeSpan Interval,
    TickBacklogPolicy BacklogPolicy);
