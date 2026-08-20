using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public sealed class TransportFrameTests
{
    [Fact]
    public void OwnerDoubleDispose_DoesNotInvalidateLiveSlice()
    {
        var owner = TransportFrame.CopyOf(new byte[] { 1, 2, 3 });
        using var slice = owner.Slice(1, 2);

        owner.Dispose();
        owner.Dispose();

        Assert.Equal(new byte[] { 2, 3 }, slice.ToArray());
        AssertDisposed(owner);
    }

    [Fact]
    public void SliceDoubleDispose_DoesNotInvalidateLiveOwner()
    {
        using var owner = TransportFrame.CopyOf(new byte[] { 1, 2, 3 });
        var slice = owner.Slice(1, 2);

        slice.Dispose();
        slice.Dispose();

        Assert.Equal(new byte[] { 1, 2, 3 }, owner.ToArray());
        AssertDisposed(slice);
    }

    [Fact]
    public void ConcurrentDispose_ReleasesOneHandleOnlyOnce()
    {
        var owner = TransportFrame.CopyOf(new byte[] { 1, 2, 3 });
        using var slice = owner.Slice(0, owner.Length);

        Parallel.For(0, 128, _ => owner.Dispose());

        Assert.Equal(new byte[] { 1, 2, 3 }, slice.ToArray());
        AssertDisposed(owner);
    }

    [Fact]
    public void EmptyFrame_RemainsUsableAfterRepeatedDispose()
    {
        var empty = TransportFrame.Empty;

        empty.Dispose();
        empty.Dispose();

        Assert.True(empty.IsEmpty);
        Assert.Empty(empty.Memory.ToArray());
        Assert.Same(TransportFrame.Empty, empty.Slice(0, 0));
    }

    private static void AssertDisposed(TransportFrame frame)
    {
        Assert.Throws<ObjectDisposedException>(() => frame.ToArray());
        Assert.Throws<ObjectDisposedException>(() => frame.Slice(0, frame.Length));
    }
}
