using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

public sealed class RpcServerLimits
{
    public int MaxConcurrentRequestsPerSession { get; set; } = 64;

    public int MaxQueuedRequestsPerSession { get; set; } = 256;

    public int MaxPendingAcceptedConnections { get; set; } = RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections;

    internal RpcServerLimits Clone()
    {
        return new RpcServerLimits
        {
            MaxConcurrentRequestsPerSession = MaxConcurrentRequestsPerSession,
            MaxQueuedRequestsPerSession = MaxQueuedRequestsPerSession,
            MaxPendingAcceptedConnections = MaxPendingAcceptedConnections
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
    }
}
