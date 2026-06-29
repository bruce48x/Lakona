using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IFeatureMessageBus
    {
        ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            TRequest request,
            CancellationToken cancellationToken = default);

        ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            string kind,
            TRequest request,
            CancellationToken cancellationToken = default);

        ValueTask<FeatureMessageReply> SendToNodeAsync<TRequest, TReply>(
            ClusterNodeDescriptor target,
            FeatureName feature,
            string kind,
            TRequest request,
            CancellationToken cancellationToken = default);
    }
}
