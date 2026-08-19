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
    private readonly int _maxFrameSize;
    private readonly int _maxBufferedBytes;
    private int _count;

    public LengthPrefixedFrameAccumulator(int maxFrameSize = LengthPrefix.DefaultMaxFrameSize)
    {
        if (maxFrameSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrameSize));

        _maxFrameSize = maxFrameSize;
        _maxBufferedBytes = checked(sizeof(uint) + maxFrameSize);
    }

    public int Count => _count;

    /// <summary>
    ///     Appends bytes received by the transport.
    /// </summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var newCount = checked(_count + data.Length);
        if (newCount > _maxBufferedBytes)
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

        var payloadLength = LengthPrefix.ReadPayloadLength(
            _buffer.AsSpan(0, 4),
            _maxFrameSize);
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
