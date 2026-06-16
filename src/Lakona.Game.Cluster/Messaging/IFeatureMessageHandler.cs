using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IFeatureMessageHandler
    {
        ValueTask<FeatureMessageReply> HandleAsync(
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default);
    }
}
