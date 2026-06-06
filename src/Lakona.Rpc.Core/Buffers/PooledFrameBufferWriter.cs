using System.Buffers;

namespace Lakona.Rpc.Core;

public sealed class PooledFrameBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[]? _buffer;
    private int _written;

    public PooledFrameBufferWriter(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, initialCapacity));
    }

    public int WrittenCount => _written;

    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (_buffer is null || _written + count > _buffer.Length)
            throw new InvalidOperationException("Cannot advance beyond the current buffer.");

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer!.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer!.AsSpan(_written);
    }

    public TransportFrame DetachFrame()
    {
        if (_buffer is null)
            return TransportFrame.Empty;

        var buffer = _buffer;
        var written = _written;
        _buffer = null;
        _written = 0;
        return TransportFrame.AdoptRented(buffer, written);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = null;
        _written = 0;
        if (buffer is not null)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeHint));

        sizeHint = Math.Max(sizeHint, 1);
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledFrameBufferWriter));
        if (buffer.Length - _written >= sizeHint)
            return;

        var required = checked(_written + sizeHint);
        var nextSize = Math.Max(buffer.Length * 2, required);
        var next = ArrayPool<byte>.Shared.Rent(nextSize);
        buffer.AsSpan(0, _written).CopyTo(next);
        ArrayPool<byte>.Shared.Return(buffer);
        _buffer = next;
    }
}
