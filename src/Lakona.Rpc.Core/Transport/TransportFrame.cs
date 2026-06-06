using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lakona.Rpc.Core;

public sealed class TransportFrame : IDisposable
{
    private static readonly TransportFrame EmptyFrame = new(null, 0, 0);

    private readonly SharedBuffer? _owner;
    private readonly int _offset;
    private readonly int _length;

    internal TransportFrame(SharedBuffer? owner, int offset, int length)
    {
        _owner = owner;
        _offset = offset;
        _length = length;
    }

    public static TransportFrame Empty => EmptyFrame;

    public int Length => _length;

    public bool IsEmpty => _length == 0;

    public byte this[int index] => Span[index];

    public ReadOnlyMemory<byte> Memory => _owner is null
        ? ReadOnlyMemory<byte>.Empty
        : _owner.GetMemory(_offset, _length);

    public ReadOnlySpan<byte> Span => Memory.Span;

    public TransportFrame Slice(int offset, int length)
    {
        if ((uint)offset > (uint)_length || (uint)length > (uint)(_length - offset))
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (length == 0)
            return Empty;

        _owner?.AddRef();
        return new TransportFrame(_owner, _offset + offset, length);
    }

    public byte[] ToArray()
    {
        return Memory.ToArray();
    }

    public void CopyTo(Span<byte> destination)
    {
        Span.CopyTo(destination);
    }

    public void CopyTo(byte[] destination, int destinationOffset)
    {
        Span.CopyTo(destination.AsSpan(destinationOffset));
    }

    public static implicit operator ReadOnlyMemory<byte>(TransportFrame frame)
    {
        return frame.Memory;
    }

    internal bool TryGetArraySegment(out ArraySegment<byte> segment)
    {
        segment = default;
        if (_owner is null)
            return false;

        return _owner.TryGetArraySegment(_offset, _length, out segment);
    }

    ~TransportFrame()
    {
        _owner?.Release();
    }

    public void Dispose()
    {
        _owner?.Release();
        GC.SuppressFinalize(this);
    }

    public static TransportFrame Allocate(int length)
    {
        if (length == 0)
            return Empty;

        return new TransportFrame(new SharedBuffer(length), 0, length);
    }

    public static TransportFrame CopyOf(ReadOnlySpan<byte> source)
    {
        var frame = Allocate(source.Length);
        if (!source.IsEmpty)
            source.CopyTo(frame.GetWritableSpan());

        return frame;
    }

    internal static TransportFrame AdoptRented(byte[] buffer, int length)
    {
        if (length == 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return Empty;
        }

        return new TransportFrame(new SharedBuffer(buffer), 0, length);
    }

    internal Span<byte> GetWritableSpan()
    {
        if (_owner is null)
            return Span<byte>.Empty;

        return _owner.GetSpan(_offset, _length);
    }

    internal Memory<byte> GetWritableMemory()
    {
        if (_owner is null)
            return Memory<byte>.Empty;

        return _owner.GetWritableMemory(_offset, _length);
    }

    internal sealed class SharedBuffer
    {
        private byte[]? _buffer;
        private int _refCount = 1;

        public SharedBuffer(int size)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(size);
        }

        public SharedBuffer(byte[] buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        public void AddRef()
        {
            if (Interlocked.Increment(ref _refCount) <= 1)
                throw new ObjectDisposedException(nameof(TransportFrame));
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) != 0)
                return;

            var buffer = Interlocked.Exchange(ref _buffer, null);
            if (buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
        }

        public ReadOnlyMemory<byte> GetMemory(int offset, int length)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(TransportFrame));
            return buffer.AsMemory(offset, length);
        }

        public Memory<byte> GetWritableMemory(int offset, int length)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(TransportFrame));
            return buffer.AsMemory(offset, length);
        }

        public Span<byte> GetSpan(int offset, int length)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(TransportFrame));
            return buffer.AsSpan(offset, length);
        }

        public bool TryGetArraySegment(int offset, int length, out ArraySegment<byte> segment)
        {
            var buffer = _buffer;
            if (buffer is null)
            {
                segment = default;
                return false;
            }

            segment = new ArraySegment<byte>(buffer, offset, length);
            return true;
        }
    }
}
