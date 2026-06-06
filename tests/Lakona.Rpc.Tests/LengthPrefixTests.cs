using System.Buffers;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public class LengthPrefixTests
{
    [Fact]
    public void Pack_ProducesCorrectHeader()
    {
        var payload = new byte[] { 0xAA, 0xBB };
        using var packed = LengthPrefix.Pack(payload);

        Assert.Equal(6, packed.Length);
        Assert.Equal(0, packed[0]);
        Assert.Equal(0, packed[1]);
        Assert.Equal(0, packed[2]);
        Assert.Equal(2, packed[3]);
        Assert.Equal(0xAA, packed[4]);
        Assert.Equal(0xBB, packed[5]);
    }

    [Fact]
    public void Pack_EmptyPayload()
    {
        using var packed = LengthPrefix.Pack(ReadOnlySpan<byte>.Empty);
        Assert.Equal(4, packed.Length);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, packed.ToArray());
    }

    [Fact]
    public void Pack_PayloadTooLarge_Throws()
    {
        var payload = GC.AllocateUninitializedArray<byte>(LengthPrefix.DefaultMaxFrameSize + 1);

        var ex = Assert.Throws<InvalidOperationException>(() => LengthPrefix.Pack(payload));
        Assert.Contains("Frame too large", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryUnpack_CompleteFrame_Succeeds()
    {
        var payload = new byte[] { 1, 2, 3 };
        using var packed = LengthPrefix.Pack(payload);
        var seq = new ReadOnlySequence<byte>(packed.Memory);

        var result = LengthPrefix.TryUnpack(ref seq, out var payloadSeq);

        Assert.True(result);
        Assert.Equal(payload, payloadSeq.ToArray());
        Assert.Equal(0, seq.Length);
    }

    [Fact]
    public void TryUnpack_IncompleteHeader_ReturnsFalse()
    {
        var seq = new ReadOnlySequence<byte>(new byte[] { 0, 0 });

        var result = LengthPrefix.TryUnpack(ref seq, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryUnpack_IncompletePayload_ReturnsFalse()
    {
        var seq = new ReadOnlySequence<byte>(new byte[] { 0, 0, 0, 5, 1, 2 });

        var result = LengthPrefix.TryUnpack(ref seq, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryUnpack_FrameTooLarge_Throws()
    {
        var data = new byte[4];
        data[0] = 0x10; // 0x10000000 = 268 MB > 64 MB
        var seq = new ReadOnlySequence<byte>(data);

        Assert.Throws<InvalidOperationException>(() =>
            LengthPrefix.TryUnpack(ref seq, out _));
    }

    [Fact]
    public void TryUnpack_CustomMaxFrameSize()
    {
        var payload = new byte[100];
        using var packed = LengthPrefix.Pack(payload);
        var seq = new ReadOnlySequence<byte>(packed.Memory);

        Assert.Throws<InvalidOperationException>(() =>
            LengthPrefix.TryUnpack(ref seq, out _, maxFrameSize: 50));
    }

    [Fact]
    public void TryUnpack_MultipleFrames_ConsumesCorrectly()
    {
        using var frame1 = LengthPrefix.Pack(new byte[] { 10 });
        using var frame2 = LengthPrefix.Pack(new byte[] { 20 });
        var combined = new byte[frame1.Length + frame2.Length];
        frame1.CopyTo(combined, 0);
        frame2.CopyTo(combined, frame1.Length);
        var seq = new ReadOnlySequence<byte>(combined);

        Assert.True(LengthPrefix.TryUnpack(ref seq, out var p1));
        Assert.Equal(new byte[] { 10 }, p1.ToArray());

        Assert.True(LengthPrefix.TryUnpack(ref seq, out var p2));
        Assert.Equal(new byte[] { 20 }, p2.ToArray());

        Assert.Equal(0, seq.Length);
    }

    [Fact]
    public void PackThenUnpack_RoundTrip_LargePayload()
    {
        var payload = new byte[8192];
        new Random(42).NextBytes(payload);
        using var packed = LengthPrefix.Pack(payload);
        var seq = new ReadOnlySequence<byte>(packed.Memory);

        Assert.True(LengthPrefix.TryUnpack(ref seq, out var result));
        Assert.Equal(payload, result.ToArray());
    }
}
