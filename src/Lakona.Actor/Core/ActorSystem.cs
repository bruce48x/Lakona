using System.Diagnostics;
using Lakona.Actor.Abstractions;
using Lakona.Actor.Core;
using Lakona.Actor.Messaging;

namespace Lakona.Actor;

public sealed class ActorSystem : IAsyncDisposable
{
    private readonly AsyncLocal<ActorCallContext?> currentCallContext = new();
    private readonly ActorRegistry registry = new();
    private readonly ActorSystemDiagnosticsPublisher diagnostics = new();
    private readonly ActorMessageDispatcher dispatcher;
    private readonly ActorSpawner spawner;
    private readonly ActorStopper stopper;
    private readonly ActorLookup lookup;
    private readonly ActorSystemOptions options;
    private bool disposed;

    public event Action<DeadLetter>? DeadLetterPublished
    {
        add => diagnostics.DeadLetterPublished += value;
        remove => diagnostics.DeadLetterPublished -= value;
    }

    public event Action<SlowMessage>? SlowMessageDetected
    {
        add => diagnostics.SlowMessageDetected += value;
        remove => diagnostics.SlowMessageDetected -= value;
    }

    public event Action<ActorCallTimeout>? CallTimedOut
    {
        add => diagnostics.CallTimedOut += value;
        remove => diagnostics.CallTimedOut -= value;
    }

    public event Action<ActorObserverError>? ObserverErrorPublished
    {
        add => diagnostics.ObserverErrorPublished += value;
        remove => diagnostics.ObserverErrorPublished -= value;
    }

    internal ActorCallContext? CurrentCallContext
    {
        get
        {
            ActorCallContext? context = currentCallContext.Value;
            return context is { IsActive: true } ? context : null;
        }
        set => currentCallContext.Value = value;
    }

    internal IActorMessageInterceptor? MessageInterceptor => options.MessageInterceptor;

    internal ActorSystemDiagnosticsPublisher Diagnostics => diagnostics;

    public ActorSystem()
        : this(new ActorSystemOptions())
    {
    }

    public ActorSystem(ActorSystemOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MailboxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MailboxCapacity must be greater than zero.");
        }

        if (options.SlowMessageThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SlowMessageThreshold must be greater than zero when set.");
        }

        this.options = options;
        dispatcher = new ActorMessageDispatcher(registry, diagnostics, () => CurrentCallContext);
        spawner = new ActorSpawner(this, registry, options);
        stopper = new ActorStopper(registry);
        lookup = new ActorLookup(this, registry);
    }

    public ValueTask<ActorHandle<TMessage>> SpawnAsync<TMessage>(IActor<TMessage> actor)
    {
        return SpawnAsync(actor, null);
    }

    public ValueTask<ActorHandle<TMessage>> SpawnAsync<TMessage>(string name, IActor<TMessage> actor)
    {
        return SpawnAsync(name, actor, null);
    }

    public async ValueTask<ActorHandle<TMessage>> SpawnAsync<TMessage>(
        string name,
        IActor<TMessage> actor,
        ActorSpawnOptions? spawnOptions)
    {
        ValidateActorName(name);
        ArgumentNullException.ThrowIfNull(actor);
        ObjectDisposedException.ThrowIf(disposed, this);

        ActorRef actorRef = await spawner.SpawnAsync(
            new TypedActorAdapter<TMessage>(actor),
            typeof(TMessage),
            spawnOptions,
            name).ConfigureAwait(false);
        return new ActorHandle<TMessage>(actorRef);
    }

    public async ValueTask<ActorHandle<TMessage>> SpawnAsync<TMessage>(IActor<TMessage> actor, ActorSpawnOptions? spawnOptions)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ObjectDisposedException.ThrowIf(disposed, this);

        ActorRef actorRef = await spawner.SpawnAsync(
            new TypedActorAdapter<TMessage>(actor),
            typeof(TMessage),
            spawnOptions,
            null).ConfigureAwait(false);
        return new ActorHandle<TMessage>(actorRef);
    }

    internal ValueTask Send(ActorId target, object message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return dispatcher.Send(target, message, cancellationToken);
    }

    internal ValueTask Send(
        ActorId target,
        object message,
        ActivityContext parentActivityContext,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return dispatcher.Send(target, message, parentActivityContext, cancellationToken);
    }

    internal ActorSendResult TrySend(
        ActorId target,
        object message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return dispatcher.TrySend(target, message);
    }

    internal ActorSendResult TrySend(
        ActorId target,
        object message,
        ActivityContext parentActivityContext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return dispatcher.TrySend(target, message, parentActivityContext);
    }

    internal ValueTask<TResponse> Call<TResponse>(
        ActorId target,
        object request,
        ActorCallOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return dispatcher.Call<TResponse>(target, request, options, cancellationToken);
    }

    public ValueTask Stop(ActorId target)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return stopper.StopAsync(target);
    }

    public ValueTask<ActorStopResult> Stop(ActorId target, TimeSpan drainTimeout)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return stopper.StopAsync(target, drainTimeout);
    }

    internal ValueTask Stop(ActorRef actorRef)
    {
        ArgumentNullException.ThrowIfNull(actorRef);

        return Stop(actorRef.Id);
    }

    internal ValueTask<ActorStopResult> Stop(ActorRef actorRef, TimeSpan drainTimeout)
    {
        ArgumentNullException.ThrowIfNull(actorRef);

        return Stop(actorRef.Id, drainTimeout);
    }

    public ValueTask Stop(string name)
    {
        ValidateActorName(name);

        if (!registry.TryGetNamed(name, out ActorId id))
        {
            return ValueTask.CompletedTask;
        }

        return Stop(id);
    }

    public ValueTask<ActorStopResult> Stop(string name, TimeSpan drainTimeout)
    {
        ValidateActorName(name);

        if (drainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout), "Drain timeout must be greater than zero.");
        }

        if (!registry.TryGetNamed(name, out ActorId id))
        {
            return ValueTask.FromResult(ActorStopResult.Drained);
        }

        return Stop(id, drainTimeout);
    }

    public MailboxMetrics GetMailboxMetrics(ActorId target)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return lookup.GetMailboxMetrics(target);
    }

    public ActorState GetActorState(ActorId target)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return lookup.GetActorState(target);
    }

    internal MailboxMetrics GetMailboxMetrics(ActorRef actorRef)
    {
        ArgumentNullException.ThrowIfNull(actorRef);

        return GetMailboxMetrics(actorRef.Id);
    }

    private bool TryGetActor(string name, out ActorRef? actorRef)
    {
        ValidateActorName(name);

        return lookup.TryGetActor(name, out actorRef);
    }

    public bool TryGetActor<TMessage>(string name, out ActorRef<TMessage>? actorRef)
    {
        if (TryGetActor(name, typeof(TMessage), out ActorRef? untyped))
        {
            actorRef = new ActorRef<TMessage>(untyped!);
            return true;
        }

        actorRef = null;
        return false;
    }

    public ActorRef<TMessage> GetActor<TMessage>(string name)
    {
        if (TryGetActor<TMessage>(name, out ActorRef<TMessage>? actorRef))
        {
            return actorRef!;
        }

        throw new InvalidOperationException($"Actor name '{name}' does not exist.");
    }

    internal bool TryGetActor(ActorId target, out ActorCell? cell)
    {
        return lookup.TryGetActor(target, out cell);
    }

    private bool TryGetActor(string name, Type messageType, out ActorRef? actorRef)
    {
        ValidateActorName(name);

        return lookup.TryGetActor(name, messageType, out actorRef);
    }

    private static void ValidateActorName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        await stopper.StopAllForDisposeAsync().ConfigureAwait(false);
    }
}
