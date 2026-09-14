namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerExecutionScope : IDisposable
{
    private static readonly AsyncLocal<LakonaTimerExecutionContext?> CurrentContext = new();
    private readonly LakonaTimerExecutionContext? previousContext;
    private bool disposed;

    private LakonaTimerExecutionScope(
        ILakonaTimerBackend backend,
        HotfixRuntimeSnapshotLease? runtimeContext)
    {
        previousContext = CurrentContext.Value;
        Context = new LakonaTimerExecutionContext(backend, runtimeContext);
        CurrentContext.Value = Context;
    }

    internal LakonaTimerExecutionContext Context { get; }

    internal static LakonaTimerExecutionContext? Current => CurrentContext.Value;

    internal static LakonaTimerExecutionContext GetActiveContext()
    {
        var context = Current;
        if (context is null || !context.IsActive)
            throw new InvalidOperationException("Lakona timers can only be used inside an active hotfix execution scope.");
        return context;
    }

    internal static LakonaTimerExecutionScope Enter(
        ILakonaTimerBackend backend,
        HotfixRuntimeSnapshotLease? runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(backend);

        return new LakonaTimerExecutionScope(backend, runtimeContext);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            Context.Deactivate();
        }
        finally
        {
            CurrentContext.Value = previousContext;
        }
    }
}

internal sealed class LakonaTimerExecutionContext
{
    internal LakonaTimerExecutionContext(
        ILakonaTimerBackend backend,
        HotfixRuntimeSnapshotLease? runtimeContext)
    {
        Backend = backend;
        RuntimeContext = runtimeContext;
        IsActive = true;
    }

    internal ILakonaTimerBackend Backend { get; }

    internal HotfixRuntimeSnapshotLease? RuntimeContext { get; }

    internal bool IsActive { get; private set; }

    internal void Deactivate()
    {
        IsActive = false;
    }
}
