using System.ComponentModel;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

/// <summary>
///     Payload-level result returned by framework control handlers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct RpcRawResult(
    RpcStatus Status,
    ReadOnlyMemory<byte> Payload,
    string? ErrorMessage = null)
{
    public static RpcRawResult Ok(ReadOnlyMemory<byte> payload)
    {
        return new RpcRawResult(RpcStatus.Ok, payload);
    }

    public static RpcRawResult Failure(RpcStatus status, string errorMessage)
    {
        if (status == RpcStatus.Ok)
            throw new ArgumentOutOfRangeException(nameof(status), status, "Failure status cannot be Ok.");

        return new RpcRawResult(status, ReadOnlyMemory<byte>.Empty, errorMessage);
    }
}
