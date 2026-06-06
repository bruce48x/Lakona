using System.Runtime.InteropServices;

namespace Lakona.Rpc.Core;

internal sealed class ReadOnlyMemoryStream : Stream
{
    private readonly ReadOnlyMemory<byte> _memory;
    private int _position;

    public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory)
    {
        _memory = memory;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _memory.Length;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _memory.Length)
                throw new ArgumentOutOfRangeException(nameof(value));

            _position = (int)value;
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var remaining = _memory.Length - _position;
        if (remaining <= 0)
            return 0;

        var toCopy = Math.Min(count, remaining);
        _memory.Span.Slice(_position, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
        _position += toCopy;
        return toCopy;
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _memory.Length - _position;
        if (remaining <= 0)
            return 0;

        var toCopy = Math.Min(buffer.Length, remaining);
        _memory.Span.Slice(_position, toCopy).CopyTo(buffer);
        _position += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _memory.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0 || target > _memory.Length)
            throw new IOException("Attempted to seek outside the bounds of the memory stream.");

        _position = (int)target;
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
