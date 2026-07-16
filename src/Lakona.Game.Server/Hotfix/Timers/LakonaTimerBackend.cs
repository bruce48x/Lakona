using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerBackend : ILakonaTimerBackend
{
    private readonly object gate = new();
    private readonly Dictionary<TimerId, LakonaTimerDescriptor> descriptors = new();
    private readonly LakonaTimerCallbackResolver callbackResolver;
    private readonly LakonaTimerArgsSerializer argsSerializer;
    private readonly LakonaTimerScheduler? scheduler;

    public LakonaTimerBackend()
        : this(new LakonaTimerCallbackResolver(), new LakonaTimerArgsSerializer())
    {
    }

    internal LakonaTimerBackend(LakonaTimerScheduler scheduler)
        : this(new LakonaTimerCallbackResolver(), new LakonaTimerArgsSerializer(), scheduler)
    {
        scheduler.AttachBackend(this);
    }

    internal LakonaTimerBackend(LakonaTimerCallbackResolver callbackResolver, LakonaTimerArgsSerializer argsSerializer)
        : this(callbackResolver, argsSerializer, scheduler: null)
    {
    }

    private LakonaTimerBackend(
        LakonaTimerCallbackResolver callbackResolver,
        LakonaTimerArgsSerializer argsSerializer,
        LakonaTimerScheduler? scheduler)
    {
        this.callbackResolver = callbackResolver ?? throw new ArgumentNullException(nameof(callbackResolver));
        this.argsSerializer = argsSerializer ?? throw new ArgumentNullException(nameof(argsSerializer));
        this.scheduler = scheduler;
    }

    public IReadOnlyCollection<LakonaTimerDescriptor> Descriptors
    {
        get
        {
            if (scheduler is not null)
            {
                return scheduler.Descriptors;
            }

            lock (gate)
            {
                return descriptors.Values.ToArray();
            }
        }
    }

    public ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TArgs args,
        CancellationToken cancellationToken)
    {
        return CreateTimerAsync(
            callback,
            dueTime,
            period: null,
            args,
            cancellationToken);
    }

    public ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TimeSpan period,
        TArgs args,
        CancellationToken cancellationToken)
    {
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        return CreateTimerAsync(
            callback,
            dueTime,
            period,
            args,
            cancellationToken);
    }

    public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scheduler is not null)
        {
            scheduler.Destroy(timerId);
            return default;
        }

        lock (gate)
        {
            descriptors.Remove(timerId);
        }

        return default;
    }

    public bool TryGetDescriptor(TimerId timerId, out LakonaTimerDescriptor descriptor)
    {
        if (scheduler is not null)
        {
            return scheduler.TryGetDescriptor(timerId, out descriptor);
        }

        lock (gate)
        {
            return descriptors.TryGetValue(timerId, out descriptor!);
        }
    }

    private ValueTask<TimerId> CreateTimerAsync<TArgs>(
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TimeSpan? period,
        TArgs args,
        CancellationToken cancellationToken)
    {
        var descriptor = CreateDescriptor(
            callback,
            dueTime,
            period,
            args,
            cancellationToken);

        AddDescriptor(descriptor);
        return new ValueTask<TimerId>(descriptor.TimerId);
    }

    public ILakonaTimerBackend CreateStagingBackend()
    {
        return new StagingTimerBackend(this);
    }

    public async ValueTask CommitStagedTimersAsync(ILakonaTimerBackend stagingBackend, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stagingBackend is not StagingTimerBackend staging || !ReferenceEquals(staging.Owner, this))
        {
            return;
        }

        var committedTimerIds = new List<TimerId>();
        try
        {
            foreach (var descriptor in staging.TakeDescriptors())
            {
                AddDescriptor(descriptor);
                committedTimerIds.Add(descriptor.TimerId);
            }
        }
        catch
        {
            foreach (var timerId in committedTimerIds)
            {
                await DestroyTimerAsync(timerId, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        return;
    }

    public ValueTask RollbackStagedTimersAsync(ILakonaTimerBackend stagingBackend, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stagingBackend is StagingTimerBackend staging && ReferenceEquals(staging.Owner, this))
        {
            staging.Clear();
        }

        return default;
    }

    private LakonaTimerDescriptor CreateDescriptor<TArgs>(
        HotfixTimerEntry<TArgs> entry,
        TimeSpan dueTime,
        TimeSpan? period,
        TArgs args,
        CancellationToken cancellationToken)
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
        var callback = callbackResolver.Validate(lease, entry);
        var serializedArgs = argsSerializer.Serialize(args);
        var timerId = TimerId.FromGuid(Guid.NewGuid());
        return new LakonaTimerDescriptor(
            timerId,
            callback.CallbackAssemblyName,
            callback.CallbackFullName,
            callback.MethodName,
            callback.MethodId,
            serializedArgs.ArgsAssemblyName,
            serializedArgs.ArgsFullName,
            serializedArgs.SerializerId,
            serializedArgs.JsonPayload,
            GetUtcNow().Add(dueTime),
            period,
            callback.Generation);
    }

    private void AddDescriptor(LakonaTimerDescriptor descriptor)
    {
        if (scheduler is not null)
        {
            scheduler.Add(descriptor);
        }
        else
        {
            lock (gate)
            {
                descriptors.Add(descriptor.TimerId, descriptor);
            }
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        return scheduler?.GetUtcNow() ?? DateTimeOffset.UtcNow;
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

    private sealed class StagingTimerBackend(LakonaTimerBackend owner) : ILakonaTimerBackend
    {
        private readonly object gate = new();
        private readonly Dictionary<TimerId, LakonaTimerDescriptor> descriptors = new();

        public LakonaTimerBackend Owner => owner;

        public ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(
            HotfixTimerEntry<TArgs> callback,
            TimeSpan dueTime,
            TArgs args,
            CancellationToken cancellationToken)
        {
            var descriptor = owner.CreateDescriptor(
                callback,
                dueTime,
                period: null,
                args,
                cancellationToken);
            lock (gate)
            {
                descriptors.Add(descriptor.TimerId, descriptor);
            }

            return new ValueTask<TimerId>(descriptor.TimerId);
        }

        public ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(
            HotfixTimerEntry<TArgs> callback,
            TimeSpan dueTime,
            TimeSpan period,
            TArgs args,
            CancellationToken cancellationToken)
        {
            if (period <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
            }

            var descriptor = owner.CreateDescriptor(
                callback,
                dueTime,
                period,
                args,
                cancellationToken);
            lock (gate)
            {
                descriptors.Add(descriptor.TimerId, descriptor);
            }

            return new ValueTask<TimerId>(descriptor.TimerId);
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

        public IReadOnlyList<LakonaTimerDescriptor> TakeDescriptors()
        {
            lock (gate)
            {
                var values = descriptors.Values.ToArray();
                descriptors.Clear();
                return values;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                descriptors.Clear();
            }
        }
    }
}
