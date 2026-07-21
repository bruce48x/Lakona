using System.Collections.Generic;

namespace Lakona.Game.Cluster
{
    public interface INodeAdvertisementProvider
    {
        IReadOnlyList<NodeAdvertisement> Describe();
    }
}
