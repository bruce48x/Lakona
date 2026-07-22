using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

public sealed record RpcSessionRequestGateContext(
    RpcConnectionInfo Connection,
    int ServiceId,
    int MethodId);

public sealed record RpcSessionRequestGateResult(
    bool Allowed,
    RpcStatus Status,
    string? ErrorMessage)
{
    public static RpcSessionRequestGateResult Allow { get; } =
        new(true, RpcStatus.Ok, null);

    public static RpcSessionRequestGateResult Deny(
        string errorMessage,
        RpcStatus status = RpcStatus.BadRequest)
    {
        return new RpcSessionRequestGateResult(false, status, errorMessage);
    }
}

public interface IRpcSessionRequestGate
{
    ValueTask<RpcSessionRequestGateResult> EvaluateAsync(
        RpcSessionRequestGateContext context,
        CancellationToken cancellationToken = default);
}
