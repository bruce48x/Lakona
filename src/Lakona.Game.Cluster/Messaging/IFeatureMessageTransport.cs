using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IFeatureMessageTransport
    {
        ValueTask<FeatureMessageReply> SendAsync(
            ClusterNodeDescriptor target,
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default);
    }
}
