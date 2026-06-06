namespace Lakona.Game.Server.Actors;

public sealed class ActorContext
{
    internal static readonly ActorContext Uninitialized = new(
        new ActorId("__uninitialized__"),
        EmptyServiceProvider.Instance,
        NullActorRuntime.Instance);

    public ActorContext(ActorId id, IServiceProvider services, IActorRuntime runtime)
    {
        Id = id;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ActorId Id { get; }

    public IServiceProvider Services { get; }

    public IActorRuntime Runtime { get; }

    public IAsyncDisposable RegisterTimer<TActor>(
        TimeSpan dueTime,
        TimeSpan? period,
        Func<TActor, CancellationToken, ValueTask> callback)
        where TActor : class, IActor
    {
        return Runtime.RegisterTimer(Id, dueTime, period, callback);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class NullActorRuntime : IActorRuntime
    {
        public static readonly NullActorRuntime Instance = new();

        public ValueTask<TActor> GetOrCreateAsync<TActor>(ActorId id, CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public ValueTask TellAsync<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public ActorTellResult TryTell<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public IAsyncDisposable RegisterTimer<TActor>(
            ActorId id,
            TimeSpan dueTime,
            TimeSpan? period,
            Func<TActor, CancellationToken, ValueTask> callback)
            where TActor : class, IActor
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public ActorState GetState(ActorId id)
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public ValueTask StopAsync(ActorId id)
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public ValueTask<ActorStopOutcome> StopAsync(ActorId id, TimeSpan drainTimeout)
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }
    }
}
