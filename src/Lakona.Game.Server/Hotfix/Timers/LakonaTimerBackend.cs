using System.Runtime.Loader;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerBackend : ILakonaTimerBackend
{
    private readonly LakonaTimerCallbackResolver callbackResolver = new();
    private readonly LakonaTimerArgsSerializer argsSerializer = new();
    private readonly LakonaTimerScheduler scheduler;

    internal LakonaTimerBackend(LakonaTimerScheduler scheduler)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        scheduler.AttachBackend(this);
    }

    public IReadOnlyCollection<LakonaTimerDescriptor> Descriptors => scheduler.Descriptors;

    public TimerId CreateTimer<TActor, TBehavior, TArgs>(
        TActor actor, Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime, TimeSpan? period, TArgs args)
        where TActor : Actors.Actor where TBehavior : class
    {
        var descriptor = CreateActorDescriptor(actor, selector, dueTime, period, args);
        scheduler.Add(descriptor);
        return descriptor.TimerId;
    }

    private LakonaTimerDescriptor CreateActorDescriptor<TActor, TBehavior, TArgs>(
        TActor actor, Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime, TimeSpan? period, TArgs args)
        where TActor : Actors.Actor where TBehavior : class
    {
        var owner = actor.Context.TimerOwner
            ?? throw new InvalidOperationException("Actor timers require a hosted activation.");
        owner.ValidateCreation();
        var lease = LakonaTimerExecutionScope.GetActiveContext().RuntimeContext
            ?? throw new InvalidOperationException("Actor timers require an active Hotfix snapshot lease.");
        var entry = lease.Snapshot.DispatchTable!.ResolveActorTimerEntry(selector);
        return CreateDescriptor(lease, entry, dueTime, period, args, owner);
    }

    public void DestroyTimer(Actors.Actor actor, TimerId timerId)
    {
        scheduler.Destroy(timerId, GetCancellationOwner(actor));
    }

    private static ActorTimerOwner GetCancellationOwner(Actors.Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var owner = actor.Context.TimerOwner
            ?? throw new InvalidOperationException("Actor timers require a hosted activation.");
        owner.ValidateTurn();
        return owner;
    }

    public bool TryGetDescriptor(TimerId timerId, out LakonaTimerDescriptor descriptor)
        => scheduler.TryGetDescriptor(timerId, out descriptor);

    private LakonaTimerDescriptor CreateDescriptor<TArgs>(
        HotfixRuntimeSnapshotLease runtimeContext,
        HotfixTimerEntry<TArgs> entry,
        TimeSpan dueTime,
        TimeSpan? period,
        TArgs args,
        ActorTimerOwner owner)
    {
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
            scheduler.GetUtcNow().Add(dueTime),
            period,
            callback.Generation) { Owner = owner };
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
