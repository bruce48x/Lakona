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
        int maxPayloadLength,
        string? responseErrorMessage,
        bool writesResponseSuffix)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        PayloadLengthOffset = payloadLengthOffset;
        PayloadStart = payloadStart;
        MaxPayloadLength = maxPayloadLength;
        ResponseErrorMessage = responseErrorMessage;
        WritesResponseSuffix = writesResponseSuffix;
    }

    internal PooledFrameBufferWriter Buffer { get; }

    internal int PayloadLengthOffset { get; }

    internal int PayloadStart { get; }

    internal int MaxPayloadLength { get; }

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
        if (count < 0 || count > MaxPayloadLength - PayloadLength)
            throw new InvalidOperationException("RPC payload exceeds the remaining envelope budget.");
        Buffer.Advance(count);
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfCompleted();
        var remaining = GetRemainingPayloadCapacity(sizeHint);
        var memory = Buffer.GetMemory(Math.Max(1, sizeHint));
        return memory.Slice(0, Math.Min(memory.Length, remaining));
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ThrowIfCompleted();
        var remaining = GetRemainingPayloadCapacity(sizeHint);
        var span = Buffer.GetSpan(Math.Max(1, sizeHint));
        return span.Slice(0, Math.Min(span.Length, remaining));
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

    private int GetRemainingPayloadCapacity(int sizeHint)
    {
        if (sizeHint < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeHint));

        var remaining = MaxPayloadLength - PayloadLength;
        if (remaining == 0 || sizeHint > remaining)
            throw new InvalidOperationException("RPC payload exceeds the remaining envelope budget.");

        return remaining;
    }
}
