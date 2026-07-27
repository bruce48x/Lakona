using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixActorLifecycleDescriptor
{
    internal HotfixActorLifecycleDescriptor(
        Type behaviorType,
        Type actorType,
        MethodInfo? startMethod,
        MethodInfo? stopMethod)
    {
        BehaviorType = behaviorType ?? throw new ArgumentNullException(nameof(behaviorType));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        StartMethod = startMethod;
        StopMethod = stopMethod;
    }

    public Type BehaviorType { get; }

    public Type ActorType { get; }

    public string? StartMethodName => StartMethod?.Name;

    public string? StopMethodName => StopMethod?.Name;

    internal MethodInfo? StartMethod { get; }

    internal MethodInfo? StopMethod { get; }
}
