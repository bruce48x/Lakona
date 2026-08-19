using System.Reflection;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tests;

internal static class FrameLimitTestAssertions
{
    public static void UsesDerivedLengthPrefixedBudgets(object transport)
    {
        var accumulatorField = transport.GetType().GetField(
            "_accumulator",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(accumulatorField);

        var accumulator = Assert.IsType<LengthPrefixedFrameAccumulator>(
            accumulatorField!.GetValue(transport));

        Assert.Equal(
            RpcProtocolLimits.DefaultMaxTransportFrameSize,
            GetPrivateField<int>(accumulator, "_maxFrameSize"));
        Assert.Equal(
            RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize,
            GetPrivateField<int>(accumulator, "_maxBufferedBytes"));
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }
}
