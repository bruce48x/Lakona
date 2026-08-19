using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public sealed class RpcProtocolLimitsTests
{
    [Fact]
    public void Defaults_UseOneEnvelopeDomainAndDerivedTransportBudgets()
    {
        Assert.Equal(64 * 1024 * 1024, RpcProtocolLimits.DefaultMaxEnvelopeSize);
        Assert.Equal(RpcProtocolLimits.DefaultMaxEnvelopeSize, RpcEnvelopeCodec.MaxEnvelopeSize);
        Assert.Equal(
            RpcProtocolLimits.DefaultMaxEnvelopeSize + RpcProtocolLimits.MaximumSecurityTransformOverhead,
            RpcProtocolLimits.DefaultMaxTransportFrameSize);
        Assert.Equal(
            sizeof(uint) + RpcProtocolLimits.DefaultMaxTransportFrameSize,
            RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize);
        Assert.Equal(RpcProtocolLimits.DefaultMaxTransportFrameSize, LengthPrefix.DefaultMaxFrameSize);
        Assert.Equal(RpcProtocolLimits.DefaultMaxEnvelopeSize, new TransportSecurityConfig().MaxDecodedFrameBytes);
    }
}
