namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

internal sealed class LakonaTimerExecutionScope : IDisposable
{
    private static readonly AsyncLocal<LakonaTimerExecutionContext?> CurrentContext = new();
    private readonly LakonaTimerExecutionContext? previousContext;
    private bool disposed;

    private LakonaTimerExecutionScope(
        ILakonaTimerBackend backend,
        IHotfixTimerEntryResolver runtimeContext)
    {
        previousContext = CurrentContext.Value;
        Context = new LakonaTimerExecutionContext(backend, runtimeContext);
        CurrentContext.Value = Context;
    }

    internal LakonaTimerExecutionContext Context { get; }

    internal static LakonaTimerExecutionContext? Current => CurrentContext.Value;

    internal static LakonaTimerExecutionScope Enter(
        ILakonaTimerBackend backend,
        IHotfixTimerEntryResolver runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(runtimeContext);

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
        IHotfixTimerEntryResolver runtimeContext)
    {
        Backend = backend;
        RuntimeContext = runtimeContext;
        IsActive = true;
    }

    internal ILakonaTimerBackend Backend { get; }

    internal IHotfixTimerEntryResolver RuntimeContext { get; }

    internal bool IsActive { get; private set; }

    internal void Deactivate()
    {
        IsActive = false;
    }
}
