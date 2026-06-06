using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public sealed class RpcProtocolLimitsTests
{
    [Fact]
    public void Defaults_AreSharedByCodecFramingAndSecurityConfig()
    {
        Assert.Equal(RpcProtocolLimits.DefaultMaxPayloadSize, RpcEnvelopeCodec.MaxPayloadSize);
        Assert.Equal(RpcProtocolLimits.DefaultMaxTransportFrameSize, LengthPrefix.DefaultMaxFrameSize);
        Assert.Equal(RpcProtocolLimits.DefaultMaxDecompressedFrameBytes, new TransportSecurityConfig().MaxDecompressedFrameBytes);
    }
}
