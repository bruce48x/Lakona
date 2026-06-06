using System.Text;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tests;

public class LengthPrefixedFrameAccumulatorTests
{
    [Fact]
    public void TryReadFrame_ReassemblesSplitFrameWithoutCopyingTail()
    {
        var accumulator = new LengthPrefixedFrameAccumulator();
        var payload = Encoding.UTF8.GetBytes("split-frame");
        using var packed = LengthPrefix.Pack(payload);

        accumulator.Append(packed.Span.Slice(0, 3), LengthPrefix.DefaultMaxFrameSize);
        Assert.False(accumulator.TryReadFrame(out _));

        accumulator.Append(packed.Span.Slice(3), LengthPrefix.DefaultMaxFrameSize);
        Assert.True(accumulator.TryReadFrame(out var frame));
        using (frame)
        {
            Assert.Equal(payload, frame.ToArray());
        }
        Assert.False(accumulator.TryReadFrame(out _));
    }

    [Fact]
    public void TryReadFrame_PreservesBufferedTailForNextFrame()
    {
        var accumulator = new LengthPrefixedFrameAccumulator();
        using var first = LengthPrefix.Pack(Encoding.UTF8.GetBytes("first"));
        using var second = LengthPrefix.Pack(Encoding.UTF8.GetBytes("second"));
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);

        accumulator.Append(combined, LengthPrefix.DefaultMaxFrameSize);

        Assert.True(accumulator.TryReadFrame(out var frame1));
        using (frame1)
        {
            Assert.Equal("first", Encoding.UTF8.GetString(frame1.Span));
        }

        Assert.True(accumulator.TryReadFrame(out var frame2));
        using (frame2)
        {
            Assert.Equal("second", Encoding.UTF8.GetString(frame2.Span));
        }
        Assert.False(accumulator.TryReadFrame(out _));
    }
}
