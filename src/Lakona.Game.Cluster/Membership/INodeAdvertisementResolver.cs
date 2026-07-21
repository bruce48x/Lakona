namespace Lakona.Game.Cluster
{
    public interface INodeAdvertisementResolver<TEndpoint>
    {
        bool TryResolve(NodeReference owner, out TEndpoint? endpoint);
    }
}
