using System.Buffers;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

/// <summary>Owns the encoded response and the metadata used to report its completion.</summary>
internal readonly struct RpcServerResponse : IDisposable
{
    private readonly TransportFrame? _frame;

    private RpcServerResponse(TransportFrame frame, RpcStatus status, string? errorMessage)
    {
        _frame = frame;
        Status = status;
        ErrorMessage = errorMessage;
    }

    public RpcStatus Status { get; }
    public string? ErrorMessage { get; }
    public ReadOnlyMemory<byte> Memory => (_frame
        ?? throw new InvalidOperationException("The RPC response is not initialized.")).Memory;

    public static RpcServerResponse Encode(
        uint requestId, RpcStatus status, ReadOnlyMemory<byte> payload, string? errorMessage = null)
    {
        // The wire format represents both null and empty error messages as absent.
        errorMessage = string.IsNullOrEmpty(errorMessage) ? null : errorMessage;
        return new(RpcEnvelopeCodec.EncodeResponse(requestId, status, payload, errorMessage), status, errorMessage);
    }

    public static RpcServerResponse Serialize<T>(uint requestId, IRpcSerializer serializer, T result)
    {
        using var writer = RpcEnvelopeCodec.BeginResponsePayload(requestId, RpcStatus.Ok);
        serializer.Serialize(writer, result);
        return new(RpcEnvelopeCodec.CompletePayload(writer), RpcStatus.Ok, null);
    }

    public static async ValueTask<RpcServerResponse> WriteAsync(
        uint requestId, Func<IBufferWriter<byte>, ValueTask> write)
    {
        using var writer = RpcEnvelopeCodec.BeginResponsePayload(requestId, RpcStatus.Ok);
        await write(writer).ConfigureAwait(false);
        return new(RpcEnvelopeCodec.CompletePayload(writer), RpcStatus.Ok, null);
    }

    public void Dispose() => _frame?.Dispose();
}
