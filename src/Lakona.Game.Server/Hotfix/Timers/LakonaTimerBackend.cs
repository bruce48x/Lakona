using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerBackend : ILakonaTimerBackend
{
    private readonly object gate = new();
    private readonly Dictionary<TimerId, LakonaTimerDescriptor> descriptors = new();
    private readonly LakonaTimerCallbackResolver callbackResolver;
    private readonly LakonaTimerArgsSerializer argsSerializer;

    public LakonaTimerBackend()
        : this(new LakonaTimerCallbackResolver(), new LakonaTimerArgsSerializer())
    {
    }

    internal LakonaTimerBackend(LakonaTimerCallbackResolver callbackResolver, LakonaTimerArgsSerializer argsSerializer)
    {
        this.callbackResolver = callbackResolver ?? throw new ArgumentNullException(nameof(callbackResolver));
        this.argsSerializer = argsSerializer ?? throw new ArgumentNullException(nameof(argsSerializer));
    }

    public IReadOnlyCollection<LakonaTimerDescriptor> Descriptors
    {
        get
        {
            lock (gate)
            {
                return descriptors.Values.ToArray();
            }
        }
    }

    public ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        return CreateTimerAsync<TCallback, TArgs>(
            dueTime,
            period: null,
            methodName,
            args,
            cancellationToken);
    }

    public ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        return CreateTimerAsync<TCallback, TArgs>(
            dueTime,
            period,
            methodName,
            args,
            cancellationToken);
    }

    public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            descriptors.Remove(timerId);
        }

        return default;
    }

    public bool TryGetDescriptor(TimerId timerId, out LakonaTimerDescriptor descriptor)
    {
        lock (gate)
        {
            return descriptors.TryGetValue(timerId, out descriptor!);
        }
    }

    private ValueTask<TimerId> CreateTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan? period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "Due time must not be negative.");
        }

        var context = LakonaTimerExecutionScope.Current;
        if (context is null || !context.IsActive)
        {
            throw new InvalidOperationException("Lakona timers can only be created inside an active hotfix execution scope.");
        }

        if (context.RuntimeContext is not HotfixRuntimeSnapshotLease lease)
        {
            throw new InvalidOperationException("Lakona timer creation requires a hotfix runtime snapshot lease.");
        }

        ValidateArgsAssembly<TArgs>(lease);
        var callback = callbackResolver.Validate<TCallback, TArgs>(lease, methodName);
        var serializedArgs = argsSerializer.Serialize(args);
        var timerId = TimerId.FromGuid(Guid.NewGuid());
        var descriptor = new LakonaTimerDescriptor(
            timerId,
            callback.CallbackAssemblyName,
            callback.CallbackFullName,
            callback.MethodName,
            serializedArgs.ArgsAssemblyName,
            serializedArgs.ArgsFullName,
            serializedArgs.SerializerId,
            serializedArgs.JsonPayload,
            DateTimeOffset.UtcNow.Add(dueTime),
            period,
            callback.Generation);

        lock (gate)
        {
            descriptors.Add(timerId, descriptor);
        }

        return new ValueTask<TimerId>(timerId);
    }

    private static void ValidateArgsAssembly<TArgs>(HotfixRuntimeSnapshotLease lease)
    {
        var snapshot = lease.Snapshot;
        var argsType = typeof(TArgs);
        if (ReferenceEquals(argsType.Assembly, snapshot.MainAssembly))
        {
            return;
        }

        if (AssemblyLoadContext.GetLoadContext(argsType.Assembly) == AssemblyLoadContext.Default)
        {
            return;
        }

        throw new InvalidOperationException($"Timer args type '{argsType.FullName}' must be from the active hotfix assembly or a shared default AssemblyLoadContext assembly.");
    }
}
