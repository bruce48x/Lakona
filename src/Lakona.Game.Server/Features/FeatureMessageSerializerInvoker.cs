using System.Reflection;
using System.Runtime.ExceptionServices;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Features;

internal static class FeatureMessageSerializerInvoker
{
    private static readonly MethodInfo SerializeMethod = typeof(IFeatureMessageSerializer)
        .GetMethods()
        .Single(method => method.Name == nameof(IFeatureMessageSerializer.Serialize));

    private static readonly MethodInfo DeserializeMethod = typeof(IFeatureMessageSerializer)
        .GetMethods()
        .Single(method => method.Name == nameof(IFeatureMessageSerializer.Deserialize));

    public static object? Deserialize(
        IFeatureMessageSerializer serializer,
        Type payloadType,
        ReadOnlyMemory<byte> payload)
    {
        try
        {
            return DeserializeMethod
                .MakeGenericMethod(payloadType)
                .Invoke(serializer, [payload]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    public static ReadOnlyMemory<byte> Serialize(
        IFeatureMessageSerializer serializer,
        Type payloadType,
        object? value)
    {
        object? result;
        try
        {
            result = SerializeMethod
                .MakeGenericMethod(payloadType)
                .Invoke(serializer, [value]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return result is ReadOnlyMemory<byte> payload
            ? payload
            : throw new InvalidOperationException("Feature message serializer returned an invalid payload.");
    }
}
