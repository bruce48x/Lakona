using System.Reflection;
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
        return DeserializeMethod
            .MakeGenericMethod(payloadType)
            .Invoke(serializer, [payload]);
    }

    public static ReadOnlyMemory<byte> Serialize(
        IFeatureMessageSerializer serializer,
        Type payloadType,
        object? value)
    {
        var result = SerializeMethod
            .MakeGenericMethod(payloadType)
            .Invoke(serializer, [value]);
        return result is ReadOnlyMemory<byte> payload
            ? payload
            : throw new InvalidOperationException("Feature message serializer returned an invalid payload.");
    }
}
