using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameHandshakeRpcGate(GameHandshakeConnectionStateRegistry states) : IRpcSessionRequestGate
{
    public ValueTask<RpcSessionRequestGateResult> EvaluateAsync(
        RpcSessionRequestGateContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.ServiceId == GameHandshakeRpc.ServiceId &&
            context.MethodId == GameHandshakeRpc.HandshakeMethodId)
        {
            return new ValueTask<RpcSessionRequestGateResult>(RpcSessionRequestGateResult.Allow);
        }

        return new ValueTask<RpcSessionRequestGateResult>(states.IsComplete(context.Connection.ConnectionId)
            ? RpcSessionRequestGateResult.Allow
            : RpcSessionRequestGateResult.Deny("HandshakeRequired"));
    }
}
