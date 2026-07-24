using Lakona.Game.Server.Internal.ActorKernel.Abstractions;
using Lakona.Game.Server.Internal.ActorKernel.Core;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed class ActorSystem : IAsyncDisposable
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
        lookup = new ActorLookup(registry);
    }

    public ValueTask<ActorHandle<TMessage>> SpawnAsync<TMessage>(IActor<TMessage> actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ObjectDisposedException.ThrowIf(disposed, this);

        ActorRef actorRef = spawner.Spawn(new TypedActorAdapter<TMessage>(actor));
        return new ValueTask<ActorHandle<TMessage>>(new ActorHandle<TMessage>(actorRef));
    }

    internal ActorSendResult TrySend(
        ActorId target,
        object message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return dispatcher.TrySend(target, message);
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
