using System.ComponentModel;

namespace Lakona.Game.Server.Hotfix.Timers;

/// <summary>
///     Runtime cooperation entry point for activating timer execution scopes.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LakonaTimerRuntime
{
    /// <summary>
    ///     Creates a framework-assigned timer identifier.
    /// </summary>
    public static TimerId CreateTimerId()
    {
        return CreateTimerId(Guid.NewGuid());
    }

    /// <summary>
    ///     Restores a framework timer identifier from its persisted value.
    /// </summary>
    public static TimerId CreateTimerId(Guid value)
    {
        return TimerId.FromGuid(value);
    }

    /// <summary>
    ///     Activates timer operations for one hotfix runtime context.
    /// </summary>
    public static IDisposable Enter(
        ILakonaTimerBackend backend,
        HotfixRuntimeSnapshotLease? runtimeContext)
    {
        return LakonaTimerExecutionScope.Enter(backend, runtimeContext);
    }
}
