using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameHandshakeRpcGate : IRpcSessionRequestGate
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

        var state = context.Session.GetOrAddScopedService(
            GameHandshakeRpc.ServiceId,
            static _ => new GameHandshakeSessionState());

        return new ValueTask<RpcSessionRequestGateResult>(state.IsComplete
            ? RpcSessionRequestGateResult.Allow
            : RpcSessionRequestGateResult.Deny("HandshakeRequired"));
    }
}
