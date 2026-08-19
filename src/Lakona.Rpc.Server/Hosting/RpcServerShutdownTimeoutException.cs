namespace Lakona.Rpc.Server;

/// <summary>
///     Indicates that an RPC host exceeded its graceful shutdown deadline.
/// </summary>
public sealed class RpcServerShutdownTimeoutException : TimeoutException
{
    public RpcServerShutdownTimeoutException(TimeSpan shutdownTimeout, int activeSessionCount)
        : base(
            $"RPC server shutdown exceeded {shutdownTimeout}; " +
            $"{activeSessionCount} active Session(s) did not complete cooperative cleanup.")
    {
        ShutdownTimeout = shutdownTimeout;
        ActiveSessionCount = activeSessionCount;
    }

    /// <summary>Gets the configured graceful shutdown timeout.</summary>
    public TimeSpan ShutdownTimeout { get; }

    /// <summary>Gets the number of active Sessions observed when the deadline expired.</summary>
    public int ActiveSessionCount { get; }
}
