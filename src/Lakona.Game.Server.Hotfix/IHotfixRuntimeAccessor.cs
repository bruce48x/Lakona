using System.ComponentModel;
using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixRuntimeAccessor
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    HotfixRuntimeSnapshot Current { get; }

    HotfixRuntimeSnapshotLease AcquireCurrent()
    {
        return Current.AcquireLease();
    }
}

public sealed class HotfixRuntimeSnapshot
{
    public HotfixRuntimeSnapshot(IHotfixServiceInvoker invoker, IServiceProvider services)
        : this(invoker, EmptyHotfixFeatureCommandInvoker.Instance, services)
    {
    }

    public HotfixRuntimeSnapshot(
        IHotfixServiceInvoker invoker,
        IHotfixFeatureCommandInvoker featureCommands,
        IServiceProvider services)
        : this(invoker, featureCommands, services, onRetired: null)
    {
    }

    internal HotfixRuntimeSnapshot(
        IHotfixServiceInvoker invoker,
        IHotfixFeatureCommandInvoker featureCommands,
        IServiceProvider services,
        Action? onRetired)
        : this(
            invoker,
            featureCommands,
            services,
            dispatchTable: null,
            hotfixServices: services,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired)
    {
    }

    internal HotfixRuntimeSnapshot(
        IHotfixServiceInvoker invoker,
        IHotfixFeatureCommandInvoker featureCommands,
        IServiceProvider services,
        HotfixDispatchTable? dispatchTable,
        IServiceProvider? hotfixServices,
        Assembly? mainAssembly,
        HotfixAssemblyLoadContext? loadContext,
        string? sourceVersion,
        string? sourcePath,
        bool ownsRuntimeResources,
        Action? onRetired)
    {
        Invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        FeatureCommands = featureCommands ?? throw new ArgumentNullException(nameof(featureCommands));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        DispatchTable = dispatchTable;
        HotfixServices = hotfixServices ?? services;
        MainAssembly = mainAssembly;
        LoadContext = loadContext;
        SourceVersion = sourceVersion;
        SourcePath = sourcePath;
        _ownsRuntimeResources = ownsRuntimeResources;
        _onRetired = onRetired;
    }

    private readonly Action? _onRetired;
    private readonly bool _ownsRuntimeResources;
    private int _referenceCount;
    private int _retired;
    private int _retirementCompleted;

    public IHotfixServiceInvoker Invoker { get; }

    public IHotfixFeatureCommandInvoker FeatureCommands { get; }

    public IServiceProvider Services { get; }

    public HotfixDispatchTable? DispatchTable { get; }

    public IServiceProvider HotfixServices { get; }

    public Assembly? MainAssembly { get; }

    internal HotfixAssemblyLoadContext? LoadContext { get; }

    public string? SourceVersion { get; }

    public string? SourcePath { get; }

    public HotfixRuntimeSnapshotLease AcquireLease()
    {
        while (true)
        {
            if (Volatile.Read(ref _retired) != 0)
            {
                throw new ObjectDisposedException(nameof(HotfixRuntimeSnapshot));
            }

            var observed = Volatile.Read(ref _referenceCount);
            if (Interlocked.CompareExchange(ref _referenceCount, observed + 1, observed) != observed)
            {
                continue;
            }

            if (Volatile.Read(ref _retired) == 0)
            {
                try
                {
                    return new HotfixRuntimeSnapshotLease(this);
                }
                catch
                {
                    ReleaseLease();
                    throw;
                }
            }

            ReleaseLease();
            throw new ObjectDisposedException(nameof(HotfixRuntimeSnapshot));
        }
    }

    internal void Retire()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 0 && Volatile.Read(ref _referenceCount) == 0)
        {
            CompleteRetirement();
        }
    }

    internal void ReleaseLease()
    {
        var remaining = Interlocked.Decrement(ref _referenceCount);
        if (remaining < 0)
        {
            throw new InvalidOperationException("Hotfix runtime snapshot lease reference count became negative.");
        }

        if (remaining == 0 && Volatile.Read(ref _retired) != 0)
        {
            CompleteRetirement();
        }
    }

    private void CompleteRetirement()
    {
        if (Interlocked.Exchange(ref _retirementCompleted, 1) != 0)
        {
            return;
        }

        if (_ownsRuntimeResources)
        {
            DisposeQuietly(HotfixServices);
            UnloadQuietly(LoadContext);
        }

        try
        {
            _onRetired?.Invoke();
        }
        catch
        {
        }
    }

    private static void UnloadQuietly(HotfixAssemblyLoadContext? loadContext)
    {
        try
        {
            loadContext?.Unload();
        }
        catch
        {
        }
    }

    private static void DisposeQuietly(IServiceProvider? provider)
    {
        try
        {
            switch (provider)
            {
                case IAsyncDisposable asyncDisposable:
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch
        {
        }
    }
}
