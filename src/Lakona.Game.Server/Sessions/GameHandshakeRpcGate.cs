using Lakona.Game.Server.Health;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameHandshakeRpcGate(
    GameHandshakeConnectionStateRegistry states,
    IGameSessionRegistry? sessions = null,
    LakonaServerReadinessState? readiness = null) : IRpcSessionRequestGate
{
    public async ValueTask<RpcSessionRequestGateResult> EvaluateAsync(
        RpcSessionRequestGateContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.ServiceId == GameHandshakeRpc.ServiceId &&
            context.MethodId == GameHandshakeRpc.HandshakeMethodId)
        {
            return RpcSessionRequestGateResult.Allow;
        }

        if (!states.IsComplete(context.Connection.ConnectionId))
            return RpcSessionRequestGateResult.Deny("HandshakeRequired");
        // Framework recovery traffic must still progress while business admission is closed.
        if (context.ServiceId == GameHandshakeRpc.ServiceId)
            return RpcSessionRequestGateResult.Allow;
        if (readiness?.ListenerFailure is not null)
            return RpcSessionRequestGateResult.Deny("NodeUnavailable", RpcStatus.InternalError);
        if (sessions is not null)
        {
            var session = await sessions.GetCurrentSessionAsync(context.Connection.ConnectionId, cancellationToken).ConfigureAwait(false);
            if (session is not null && await sessions.IsReliableReplayPendingAsync(session.Value, cancellationToken).ConfigureAwait(false))
                return RpcSessionRequestGateResult.Deny("ReliableReplayPending");
        }
        return RpcSessionRequestGateResult.Allow;
    }
}
