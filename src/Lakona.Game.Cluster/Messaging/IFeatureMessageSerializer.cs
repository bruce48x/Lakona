namespace Lakona.Game.Cluster
{
    public interface IFeatureMessageSerializer
    {
        System.ReadOnlyMemory<byte> Serialize<T>(T value);

        T Deserialize<T>(System.ReadOnlyMemory<byte> payload);
    }
}
