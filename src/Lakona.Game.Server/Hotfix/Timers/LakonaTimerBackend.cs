using System.Runtime.Loader;
using Lakona.Game.Server.Actors;

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

    public TimerId CreateTimer<TActor, TBehavior, TArgs>(
        TActor actor, Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime, TimeSpan? period, TArgs args, CancellationToken cancellationToken)
        where TActor : Actors.Actor where TBehavior : class
    {
        var descriptor = CreateActorDescriptor(actor, selector, dueTime, period, args, cancellationToken);
        AddDescriptor(descriptor);
        return descriptor.TimerId;
    }

    private LakonaTimerDescriptor CreateActorDescriptor<TActor, TBehavior, TArgs>(
        TActor actor, Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime, TimeSpan? period, TArgs args, CancellationToken cancellationToken)
        where TActor : Actors.Actor where TBehavior : class
    {
        var owner = actor.Context.TimerOwner
            ?? throw new InvalidOperationException("Actor timers require a hosted activation.");
        owner.ValidateCreation();
        var lease = LakonaTimerExecutionScope.GetActiveContext().RuntimeContext
            ?? throw new InvalidOperationException("Actor timers require an active Hotfix snapshot lease.");
        var entry = lease.Snapshot.DispatchTable!.ResolveActorTimerEntry(selector);
        return CreateDescriptor(lease, entry, dueTime, period, args, cancellationToken, owner);
    }

    public void DestroyTimer(Actors.Actor actor, TimerId timerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DestroyTimer(timerId, GetCancellationOwner(actor));
    }

    private static ActorTimerOwner GetCancellationOwner(Actors.Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var owner = actor.Context.TimerOwner
            ?? throw new InvalidOperationException("Actor timers require a hosted activation.");
        owner.ValidateTurn();
        return owner;
    }

    private void DestroyTimer(TimerId timerId, ActorTimerOwner owner)
    {
        if (scheduler is not null)
        {
            scheduler.Destroy(timerId, owner);
            return;
        }

        lock (gate)
        {
            if (descriptors.TryGetValue(timerId, out var descriptor)
                && !ReferenceEquals(descriptor.Owner, owner))
                throw new InvalidOperationException("The timer belongs to another Actor activation.");
            descriptors.Remove(timerId);
        }
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

    private LakonaTimerDescriptor CreateDescriptor<TArgs>(
        HotfixRuntimeSnapshotLease runtimeContext,
        HotfixTimerEntry<TArgs> entry,
        TimeSpan dueTime,
        TimeSpan? period,
        TArgs args,
        CancellationToken cancellationToken,
        ActorTimerOwner owner)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "Due time must not be negative.");
        }

        if (period is { } interval && interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(period));
        var lease = runtimeContext;
        ValidateArgsAssembly<TArgs>(lease);
        var callback = callbackResolver.Validate(lease, entry);
        var serializedArgs = argsSerializer.Serialize(args);
        var timerId = LakonaTimerRuntime.CreateTimerId();
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
            callback.Generation) { Owner = owner };
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
}
