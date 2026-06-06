using System.Buffers;

namespace Lakona.Rpc.Core;

internal sealed class PooledBufferStream : Stream
{
    private byte[] _buffer;
    private int _length;
    private int _position;
    private bool _detached;
    private bool _disposed;

    public PooledBufferStream(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, initialCapacity));
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateWriteArgs(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        EnsureCapacity(_position + buffer.Length);
        buffer.CopyTo(_buffer.AsSpan(_position));
        _position += buffer.Length;
        if (_position > _length)
            _length = _position;
    }

    public TransportFrame DetachFrame()
    {
        ThrowIfDisposed();
        _detached = true;
        var buffer = _buffer;
        _buffer = Array.Empty<byte>();
        _disposed = true;
        return TransportFrame.AdoptRented(buffer, _length);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_detached && _buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(_buffer);

        _buffer = Array.Empty<byte>();
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    private void EnsureCapacity(int required)
    {
        if (_buffer.Length >= required)
            return;

        var newBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PooledBufferStream));
    }

    private static void ValidateWriteArgs(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException(nameof(offset));
    }
}
