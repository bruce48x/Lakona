namespace Lakona.Game.Server.Actors;

public interface IRemoteActorSerializer
{
    ReadOnlyMemory<byte> Serialize<T>(T value);
    T Deserialize<T>(ReadOnlyMemory<byte> payload);
}
