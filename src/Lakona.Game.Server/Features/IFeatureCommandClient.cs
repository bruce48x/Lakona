using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Features;

public interface IFeatureCommandClient
{
    ValueTask<TReply> SendAsync<TRequest, TReply>(
        string featureName,
        TRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<TReply> SendToNodeAsync<TRequest, TReply>(
        ClusterNodeDescriptor target,
        string featureName,
        TRequest request,
        CancellationToken cancellationToken = default);
}
