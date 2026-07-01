namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

internal sealed class LakonaTimerExecutionScope : IDisposable
{
    private static readonly AsyncLocal<LakonaTimerExecutionContext?> CurrentContext = new();
    private readonly LakonaTimerExecutionContext? previousContext;
    private bool disposed;

    private LakonaTimerExecutionScope(ILakonaTimerBackend backend, object runtimeContext)
    {
        previousContext = CurrentContext.Value;
        Context = new LakonaTimerExecutionContext(backend, runtimeContext);
        CurrentContext.Value = Context;
    }

    internal LakonaTimerExecutionContext Context { get; }

    internal static LakonaTimerExecutionContext? Current => CurrentContext.Value;

    internal static LakonaTimerExecutionScope Enter(ILakonaTimerBackend backend, object runtimeContext)
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
    internal LakonaTimerExecutionContext(ILakonaTimerBackend backend, object runtimeContext)
    {
        Backend = backend;
        RuntimeContext = runtimeContext;
        IsActive = true;
    }

    internal ILakonaTimerBackend Backend { get; }

    internal object RuntimeContext { get; }

    internal bool IsActive { get; private set; }

    internal void Deactivate()
    {
        IsActive = false;
    }
}
