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
    private const int RetainedBufferLimit = 64 * 1024;
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

        EnsureCapacity(newCount, data);
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

        if (_buffer.Length > RetainedBufferLimit && payloadLength > RetainedBufferLimit)
        {
            // Hand off the completed payload without a second full-frame allocation.
            // Only the following bytes belong to this accumulator after the handoff.
            var completed = _buffer;
            var remaining = _count - totalLength;
            var tail = remaining == 0 ? Array.Empty<byte>() : new byte[remaining];
            completed.AsSpan(totalLength, remaining).CopyTo(tail);
            frame = TransportFrame.AdoptOwned(completed, 4, payloadLength);
            _buffer = tail;
            _count = remaining;
            return true;
        }

        frame = TransportFrame.Allocate(payloadLength);
        if (payloadLength > 0)
            _buffer.AsSpan(4, payloadLength).CopyTo(frame.GetWritableSpan());

        Consume(totalLength);
        return true;
    }

    /// <summary>Discards buffered bytes, releasing any in-progress large frame. Not thread-safe.</summary>
    public void Clear()
    {
        _buffer = Array.Empty<byte>();
        _count = 0;
    }

    private void Consume(int count)
    {
        var remaining = _count - count;
        if (remaining > 0)
            _buffer.AsSpan(count, remaining).CopyTo(_buffer);

        _count = remaining;
        if (remaining == 0 && _buffer.Length > RetainedBufferLimit)
            _buffer = Array.Empty<byte>();
    }

    private void EnsureCapacity(int requiredCapacity, ReadOnlySpan<byte> incoming)
    {
        if (_buffer.Length >= requiredCapacity)
            return;

        var newCapacity = _buffer.Length == 0 ? 256 : _buffer.Length;
        while (newCapacity < requiredCapacity)
        {
            // Once at least 1 MiB is buffered, fewer growth copies reduce both LOH
            // allocation and garbage. The known frame length below still caps growth;
            // a prefix alone never triggers a payload-sized allocation.
            var factor = newCapacity >= 1024 * 1024 ? 4 : 2;
            newCapacity = (int)Math.Min((long)newCapacity * factor, _maxBufferedBytes);
        }

        // A prefix alone never reserves the advertised payload. Growth follows received
        // bytes, but does not round a known large frame up to the next power of two.
        var limit = _maxBufferedBytes;
        if (_count >= 4)
            limit = checked(4 + LengthPrefix.ReadPayloadLength(_buffer.AsSpan(0, 4), _maxFrameSize));
        else if (_count == 0 && incoming.Length >= 4)
            limit = checked(4 + LengthPrefix.ReadPayloadLength(incoming.Slice(0, 4), _maxFrameSize));
        newCapacity = Math.Min(newCapacity, limit > RetainedBufferLimit
            ? Math.Max(requiredCapacity, limit) : _maxBufferedBytes);
        Array.Resize(ref _buffer, newCapacity);
    }
}
