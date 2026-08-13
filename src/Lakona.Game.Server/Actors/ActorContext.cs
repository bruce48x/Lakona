namespace Lakona.Game.Server.Actors;

/// <summary>
/// Provides runtime services to a hosted actor instance.
/// </summary>
public sealed class ActorContext
{
    internal static readonly ActorContext Uninitialized = new(
        new ActorId("__uninitialized__"),
        EmptyServiceProvider.Instance,
        NullActorRuntime.Instance);

    /// <summary>
    /// Initializes a new actor context.
    /// </summary>
    /// <param name="id">The hosted actor id.</param>
    /// <param name="services">The service provider available to the actor.</param>
    /// <param name="runtime">The local actor runtime.</param>
    public ActorContext(ActorId id, IServiceProvider services, IActorRuntime runtime)
    {
        Id = id;
        Key = ActorIdentity.GetKey(id);
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>
    /// Gets the stable id of the hosted actor instance.
    /// </summary>
    public ActorId Id { get; }

    /// <summary>
    /// Gets the decoded business key portion of <see cref="Id"/>.
    /// </summary>
    /// <remarks>
    /// For an actor id such as <c>room/a%2Fb</c>, this value is <c>a/b</c>.
    /// Actor behavior should use this property instead of treating the full,
    /// type-qualified actor id as a business key.
    /// </remarks>
    public string Key { get; }

    /// <summary>
    /// Gets the service provider available to actor behavior and lifecycle code.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the process-local actor runtime.
    /// </summary>
    /// <remarks>
    /// Ordinary gameplay code should prefer generated actor references. The raw
    /// runtime is mainly for framework integration and advanced local dispatch.
    /// </remarks>
    public IActorRuntime Runtime { get; }

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

        public ValueTask TellAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Actor context is not initialized.");
        }

        public ActorTellResult TryTell(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
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

        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
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

    }
}
