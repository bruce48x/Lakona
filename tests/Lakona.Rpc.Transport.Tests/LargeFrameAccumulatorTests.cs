using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tests;

public sealed class LargeFrameAccumulatorTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    public void Incremental_large_receive_stays_within_allocation_budget(int mebibytes)
    {
        var payload = new byte[mebibytes * 1024 * 1024];
        using var packed = LengthPrefix.Pack(payload);
        var accumulator = new LengthPrefixedFrameAccumulator();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int offset = 0; offset < packed.Length; offset += 64 * 1024)
            accumulator.Append(packed.Span.Slice(offset, Math.Min(64 * 1024, packed.Length - offset)));
        Assert.True(accumulator.TryReadFrame(out var frame));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        using (frame) Assert.Equal(payload.Length, frame.Length);
        // Representative 12/24 MiB transfers must not spend another payload on
        // avoidable geometric-growth garbage in addition to the final storage.
        Assert.InRange(allocated, payload.Length, 2L * payload.Length + 64 * 1024);
    }

    [Fact]
    public void Completed_large_frame_transfers_storage_and_preserves_tail_and_slice_lifetime()
    {
        var payload = new byte[12 * 1024 * 1024];
        new Random(42).NextBytes(payload);
        using var packed = LengthPrefix.Pack(payload);
        using var tail = LengthPrefix.Pack(new byte[] { 7, 8, 9 });
        var accumulator = new LengthPrefixedFrameAccumulator();
        // Includes a split header, incremental growth and trailing bytes from another frame.
        accumulator.Append(packed.Span.Slice(0, 3));
        for (int offset = 3; offset < packed.Length;)
        {
            int length = Math.Min(64 * 1024, packed.Length - offset);
            accumulator.Append(packed.Span.Slice(offset, length));
            offset += length;
        }
        accumulator.Append(tail.Span);
        var accumulatedStorage = typeof(LengthPrefixedFrameAccumulator)
            .GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(accumulator);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(accumulator.TryReadFrame(out var frame));
        // Completing a large frame must not allocate another payload-sized buffer.
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 64 * 1024);
        Assert.True(MemoryMarshal.TryGetArray(frame.Memory, out var storage));
        Assert.Same(accumulatedStorage, storage.Array);
        using var slice = frame.Slice(1, frame.Length - 2);
        frame.Dispose();
        Assert.True(accumulator.TryReadFrame(out var marker));
        using (marker) Assert.Equal(new byte[] { 7, 8, 9 }, marker.ToArray());
        accumulator.Clear();
        accumulator.Append(tail.Span);
        Assert.True(slice.Span.SequenceEqual(payload.AsSpan(1, payload.Length - 2)));
        Assert.True(accumulator.TryReadFrame(out var next));
        next.Dispose();
    }

    [Fact]
    public void Header_does_not_reserve_advertised_large_payload_and_clear_discards_partial_frame()
    {
        var accumulator = new LengthPrefixedFrameAccumulator();
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, 64 * 1024 * 1024);
        long before = GC.GetAllocatedBytesForCurrentThread();
        accumulator.Append(header);
        Assert.False(accumulator.TryReadFrame(out _));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 64 * 1024);
        accumulator.Append(new byte[1024]);
        accumulator.Clear();
        Assert.Equal(0, accumulator.Count);
        using var packed = LengthPrefix.Pack(new byte[] { 42 });
        accumulator.Append(packed.Span);
        Assert.True(accumulator.TryReadFrame(out var frame));
        using (frame) Assert.Equal(new byte[] { 42 }, frame.ToArray());
    }
}
