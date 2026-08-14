namespace Lakona.Game.Server.Actors;

internal sealed class ActorCompensationLifetime
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan timeout;
    private readonly TimeProvider timeProvider;

    public ActorCompensationLifetime()
        : this(DefaultTimeout, TimeProvider.System)
    {
    }

    internal ActorCompensationLifetime(TimeSpan timeout, TimeProvider timeProvider)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Actor compensation timeout must be greater than zero.");

        this.timeout = timeout;
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask ExecuteAsync(
        ActorId actorId,
        string operation,
        Func<CancellationToken, ValueTask> compensation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(compensation);

        using var deadline = new CancellationTokenSource(timeout, timeProvider);
        try
        {
            await compensation(deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            throw new ActorCompensationTimeoutException(actorId, operation, timeout, exception);
        }
    }
}

internal sealed class ActorCompensationTimeoutException : TimeoutException
{
    public ActorCompensationTimeoutException(
        ActorId actorId,
        string operation,
        TimeSpan timeout,
        Exception innerException)
        : base(
            $"Actor compensation '{operation}' for actor id '{actorId.Value}' did not finish within {timeout}.",
            innerException)
    {
        ActorId = actorId;
        Operation = operation;
        Timeout = timeout;
    }

    public ActorId ActorId { get; }

    public string Operation { get; }

    public TimeSpan Timeout { get; }
}
