using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

public sealed class RpcServerLimits
{
    /// <summary>Default maximum number of active RPC connections per host.</summary>
    public const int DefaultMaxActiveConnections = 10_000;

    public int MaxConcurrentRequestsPerSession { get; set; } = 64;

    public int MaxQueuedRequestsPerSession { get; set; } = 256;

    public int MaxPendingAcceptedConnections { get; set; } = RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections;

    /// <summary>Gets or sets the hard maximum number of active RPC connections.</summary>
    public int MaxActiveConnections { get; set; } = DefaultMaxActiveConnections;

    internal RpcServerLimits Clone()
    {
        return new RpcServerLimits
        {
            MaxConcurrentRequestsPerSession = MaxConcurrentRequestsPerSession,
            MaxQueuedRequestsPerSession = MaxQueuedRequestsPerSession,
            MaxPendingAcceptedConnections = MaxPendingAcceptedConnections,
            MaxActiveConnections = MaxActiveConnections
        };
    }

    internal void Validate()
    {
        if (MaxConcurrentRequestsPerSession <= 0)
            throw new InvalidOperationException("MaxConcurrentRequestsPerSession must be positive.");

        if (MaxQueuedRequestsPerSession < 0)
            throw new InvalidOperationException("MaxQueuedRequestsPerSession cannot be negative.");

        if (MaxPendingAcceptedConnections <= 0)
            throw new InvalidOperationException("MaxPendingAcceptedConnections must be positive.");

        if (MaxActiveConnections <= 0)
            throw new InvalidOperationException("MaxActiveConnections must be positive.");
    }
}
