using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lakona.Rpc.Core;

/// <summary>
///     Owns one disposable lease over a transport frame buffer.
/// </summary>
/// <remarks>
///     Slices own independent leases over the shared buffer. Disposing a frame is idempotent and does not
///     invalidate another live slice. Buffer access through a disposed non-empty frame throws
///     <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class TransportFrame : IDisposable
{
    private static readonly TransportFrame EmptyFrame = new(null, 0, 0);

    private readonly SharedBuffer? _owner;
    private readonly int _offset;
    private readonly int _length;
    private int _released;

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

    public ReadOnlyMemory<byte> Memory => GetOwnerForAccess() is not { } owner
        ? ReadOnlyMemory<byte>.Empty
        : owner.GetMemory(_offset, _length);

    public ReadOnlySpan<byte> Span => Memory.Span;

    public TransportFrame Slice(int offset, int length)
    {
        var owner = GetOwnerForAccess();
        if ((uint)offset > (uint)_length || (uint)length > (uint)(_length - offset))
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (length == 0)
            return Empty;

        owner!.AddRef();
        return new TransportFrame(owner, _offset + offset, length);
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
        var owner = GetOwnerForAccess();
        if (owner is null)
            return false;

        return owner.TryGetArraySegment(_offset, _length, out segment);
    }

    ~TransportFrame()
    {
        ReleaseOnce();
    }

    public void Dispose()
    {
        ReleaseOnce();
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
        var owner = GetOwnerForAccess();
        if (owner is null)
            return Span<byte>.Empty;

        return owner.GetSpan(_offset, _length);
    }

    internal Memory<byte> GetWritableMemory()
    {
        var owner = GetOwnerForAccess();
        if (owner is null)
            return Memory<byte>.Empty;

        return owner.GetWritableMemory(_offset, _length);
    }

    private SharedBuffer? GetOwnerForAccess()
    {
        if (_owner is null)
            return null;

        if (Volatile.Read(ref _released) != 0)
            throw new ObjectDisposedException(nameof(TransportFrame));

        return _owner;
    }

    private void ReleaseOnce()
    {
        if (_owner is not null && Interlocked.Exchange(ref _released, 1) == 0)
            _owner.Release();
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
            while (true)
            {
                var current = Volatile.Read(ref _refCount);
                if (current <= 0)
                    throw new ObjectDisposedException(nameof(TransportFrame));

                if (current == int.MaxValue)
                    throw new InvalidOperationException("Transport frame reference count overflowed.");

                if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                    return;
            }
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
