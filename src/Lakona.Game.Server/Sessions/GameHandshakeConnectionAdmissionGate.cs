using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameHandshakeConnectionAdmissionGate : IRpcSessionAdmissionGate
{
    private readonly GameHandshakeConnectionStateRegistry _states;
    private readonly LakonaGameEndpointConnectionLimitsOptions _limits;
    private readonly SemaphoreSlim _pendingHandshakes;
    private readonly ILogger _logger;

    public GameHandshakeConnectionAdmissionGate(
        GameHandshakeConnectionStateRegistry states,
        LakonaGameEndpointConnectionLimitsOptions limits,
        ILogger logger)
    {
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (limits.MaxPendingHandshakes <= 0)
            throw new InvalidOperationException("MaxPendingHandshakes must be positive.");
        if (limits.HandshakeTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("HandshakeTimeout must be positive.");
        if (limits.MaxPendingHandshakes > limits.MaxActiveConnections)
            throw new InvalidOperationException("MaxPendingHandshakes cannot exceed MaxActiveConnections.");

        _pendingHandshakes = new SemaphoreSlim(
            limits.MaxPendingHandshakes,
            limits.MaxPendingHandshakes);
    }

    public ValueTask<RpcSessionAdmissionResult> EvaluateAsync(
        RpcSessionAdmissionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_pendingHandshakes.Wait(0))
        {
            return new ValueTask<RpcSessionAdmissionResult>(
                RpcSessionAdmissionResult.Deny("PendingHandshakeLimit"));
        }

        var lease = _states.RegisterPending(
            context.ConnectionId,
            _limits.HandshakeTimeout,
            _pendingHandshakes,
            _logger);
        return new ValueTask<RpcSessionAdmissionResult>(
            RpcSessionAdmissionResult.Allow(lease.SessionCancellation, lease));
    }
}
