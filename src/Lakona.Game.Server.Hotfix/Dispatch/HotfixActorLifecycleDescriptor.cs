using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixActorLifecycleDescriptor
{
    internal HotfixActorLifecycleDescriptor(
        Type actorType,
        MethodInfo? startMethod,
        MethodInfo? stopMethod)
    {
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        StartMethod = startMethod;
        StopMethod = stopMethod;
    }

    public Type ActorType { get; }

    public string? StartMethodName => StartMethod?.Name;

    public string? StopMethodName => StopMethod?.Name;

    internal MethodInfo? StartMethod { get; }

    internal MethodInfo? StopMethod { get; }
}
