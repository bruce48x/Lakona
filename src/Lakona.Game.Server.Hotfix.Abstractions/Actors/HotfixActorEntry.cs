namespace Lakona.Game.Server.Hotfix.Abstractions.Actors;

/// <summary>
/// Identifies a generated resultless hotfix actor entry without retaining a
/// delegate or an object from the hotfix assembly.
/// </summary>
public readonly record struct HotfixActorEntry<TActor, TRequest>(
    string MethodName,
    ulong MethodId,
    bool PassCancellationToken);

/// <summary>
/// Identifies a generated result-bearing hotfix actor entry without retaining
/// a delegate or an object from the hotfix assembly.
/// </summary>
public readonly record struct HotfixActorEntry<TActor, TRequest, TResult>(
    string MethodName,
    ulong MethodId,
    bool PassCancellationToken);
