using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Features;

public sealed class RpcFeatureMessageSerializer : IFeatureMessageSerializer
{
    private readonly IRpcSerializer _serializer;

    public RpcFeatureMessageSerializer(IRpcSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        using var frame = _serializer.SerializeFrame(value);
        return frame.Memory.ToArray();
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        return _serializer.Deserialize<T>(payload);
    }
}
