using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Actors;

public sealed class RpcRemoteActorSerializer : IRemoteActorSerializer
{
    private readonly IRpcSerializer _serializer;

    public RpcRemoteActorSerializer(IRpcSerializer serializer)
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
