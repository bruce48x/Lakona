namespace Lakona.Game.Server.Actors;

/// <summary>
/// Owns the local, monotonic lifetime of one cluster invocation.
/// </summary>
internal sealed class ClusterInvocationLifetime : IDisposable
{
    private readonly TimeProvider timeProvider;
    private readonly long startedAt;
    private readonly TimeSpan initialTimeToLive;
    private readonly CancellationTokenSource cancellationSource;
    private readonly ITimer timer;
    private int timedOut;

    private ClusterInvocationLifetime(
        TimeSpan timeToLive,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeToLive),
                "Cluster invocation time-to-live must be positive.");
        }

        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        initialTimeToLive = timeToLive;
        startedAt = timeProvider.GetTimestamp();
        cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer = timeProvider.CreateTimer(
            static state => ((ClusterInvocationLifetime)state!).OnTimeout(),
            this,
            timeToLive,
            Timeout.InfiniteTimeSpan);
    }

    public CancellationToken Token => cancellationSource.Token;

    public TimeSpan Remaining
    {
        get
        {
            var elapsed = timeProvider.GetElapsedTime(startedAt);
            var remaining = initialTimeToLive - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public static ClusterInvocationLifetime FromDeadline(
        DateTimeOffset deadline,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var remaining = deadline - timeProvider.GetUtcNow();
        return new ClusterInvocationLifetime(remaining, timeProvider, cancellationToken);
    }

    public static ClusterInvocationLifetime FromTimeToLive(
        TimeSpan timeToLive,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default) =>
        new(timeToLive, timeProvider, cancellationToken);

    public RemoteActorInvocationResult ToCancellationResult(
        CancellationToken callerCancellationToken,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return callerCancellationToken.IsCancellationRequested
            ? RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Cancelled,
                exception.Message,
                RemoteActorRetrySafety.Indeterminate)
            : RemoteActorInvocationResult.Failed(
                RemoteActorStatus.Timeout,
                exception.Message,
                RemoteActorRetrySafety.Indeterminate);
    }

    public void Dispose()
    {
        timer.Dispose();
        cancellationSource.Dispose();
    }

    private void OnTimeout()
    {
        if (Interlocked.Exchange(ref timedOut, 1) != 0)
        {
            return;
        }

        try
        {
            cancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
