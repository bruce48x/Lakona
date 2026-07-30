using System.Buffers;
using System.ComponentModel;

namespace Lakona.Rpc.Core;

/// <summary>
/// Writes one opaque RPC payload directly into its final envelope buffer.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcEnvelopePayloadWriter : IBufferWriter<byte>, IDisposable
{
    private int _completed;

    internal RpcEnvelopePayloadWriter(
        PooledFrameBufferWriter buffer,
        int payloadLengthOffset,
        int payloadStart,
        string? responseErrorMessage,
        bool writesResponseSuffix)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        PayloadLengthOffset = payloadLengthOffset;
        PayloadStart = payloadStart;
        ResponseErrorMessage = responseErrorMessage;
        WritesResponseSuffix = writesResponseSuffix;
    }

    internal PooledFrameBufferWriter Buffer { get; }

    internal int PayloadLengthOffset { get; }

    internal int PayloadStart { get; }

    internal string? ResponseErrorMessage { get; }

    internal bool WritesResponseSuffix { get; }

    /// <summary>
    /// Gets the number of payload bytes written so far.
    /// </summary>
    public int PayloadLength => Buffer.WrittenCount - PayloadStart;

    /// <inheritdoc />
    public void Advance(int count)
    {
        ThrowIfCompleted();
        Buffer.Advance(count);
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfCompleted();
        return Buffer.GetMemory(sizeHint);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ThrowIfCompleted();
        return Buffer.GetSpan(sizeHint);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Buffer.Dispose();
    }

    internal void MarkCompleted()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            throw new InvalidOperationException("The RPC envelope payload writer is already complete.");
        }
    }

    private void ThrowIfCompleted()
    {
        if (Volatile.Read(ref _completed) != 0)
        {
            throw new InvalidOperationException("The RPC envelope payload writer is already complete.");
        }
    }
}
