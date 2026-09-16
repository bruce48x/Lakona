namespace Lakona.Game.Server.Hotfix;

/// <summary>Identifies a lifecycle call and carries its event data.</summary>
/// <remarks>Inject dependencies into the lifecycle implementation constructor.</remarks>
public readonly struct HotfixLifecycleCall<TRequest>
{
    public HotfixLifecycleCall(TRequest request) => Request = request;

    public TRequest Request { get; }
}
