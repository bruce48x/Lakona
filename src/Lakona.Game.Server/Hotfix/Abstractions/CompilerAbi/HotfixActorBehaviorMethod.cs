namespace Lakona.Game.Server.Hotfix.Abstractions.Actors;

public readonly record struct HotfixActorBehaviorMethod(
    string MethodName,
    ulong RemoteMethodId,
    bool PassCancellationToken);
