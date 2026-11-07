using System.Reflection;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tests;

internal static class FrameLimitTestAssertions
{
    public static void UsesDerivedLengthPrefixedBudgets(object transport)
    {
        var accumulator = GetAccumulator(transport);

        Assert.Equal(
            RpcProtocolLimits.DefaultMaxTransportFrameSize,
            GetPrivateField<int>(accumulator, "_maxFrameSize"));
        Assert.Equal(
            RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize,
            GetPrivateField<int>(accumulator, "_maxBufferedBytes"));
    }

    public static LengthPrefixedFrameAccumulator GetAccumulator(object transport)
    {
        // WebSocket's shared framing owner also coordinates receive/dispose.
        var framing = transport.GetType().GetField("_framing", BindingFlags.Instance | BindingFlags.NonPublic);
        return GetPrivateField<LengthPrefixedFrameAccumulator>(framing?.GetValue(transport) ?? transport, "_accumulator");
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }
}
