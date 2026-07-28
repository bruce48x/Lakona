using System.Buffers.Binary;

namespace Lakona.Rpc.Core;

/// <summary>
///     Incrementally reconstructs length-prefixed RPC transport frames from an arbitrary byte stream.
/// </summary>
/// <remarks>
///     Transport adapters own one decoder per connection. Appended bytes may contain a partial frame,
///     one complete frame, or several frames.
/// </remarks>
public sealed class LengthPrefixedFrameAccumulator
{
    private byte[] _buffer = Array.Empty<byte>();
    private int _count;

    public int Count => _count;

    /// <summary>
    ///     Appends bytes received by the transport.
    /// </summary>
    public void Append(ReadOnlySpan<byte> data, int maxBufferedBytes)
    {
        if (data.IsEmpty)
            return;

        var newCount = checked(_count + data.Length);
        if (newCount > maxBufferedBytes)
            throw new InvalidOperationException("Frame buffer exceeded maximum size.");

        EnsureCapacity(newCount);
        data.CopyTo(_buffer.AsSpan(_count));
        _count = newCount;
    }

    /// <summary>
    ///     Removes and returns the next complete frame when one is available.
    /// </summary>
    public bool TryReadFrame(out TransportFrame frame)
    {
        frame = TransportFrame.Empty;
        if (_count < 4)
            return false;

        var frameLength = BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(0, 4));
        if (frameLength > LengthPrefix.DefaultMaxFrameSize)
            throw new InvalidOperationException($"Frame too large: {frameLength} bytes");

        var payloadLength = checked((int)frameLength);
        var totalLength = checked(4 + payloadLength);
        if (_count < totalLength)
            return false;

        frame = TransportFrame.Allocate(payloadLength);
        if (payloadLength > 0)
            _buffer.AsSpan(4, payloadLength).CopyTo(frame.GetWritableSpan());

        Consume(totalLength);
        return true;
    }

    private void Consume(int count)
    {
        var remaining = _count - count;
        if (remaining > 0)
            _buffer.AsSpan(count, remaining).CopyTo(_buffer);

        _count = remaining;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (_buffer.Length >= requiredCapacity)
            return;

        var newCapacity = _buffer.Length == 0 ? 256 : _buffer.Length;
        while (newCapacity < requiredCapacity)
            newCapacity = checked(newCapacity * 2);

        Array.Resize(ref _buffer, newCapacity);
    }
}
