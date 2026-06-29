using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixFeatureMessageHandler : IFeatureMessageHandler
{
    private readonly IHotfixRuntimeAccessor _hotfixRuntime;

    public HotfixFeatureMessageHandler(IHotfixRuntimeAccessor hotfixRuntime)
    {
        _hotfixRuntime = hotfixRuntime ?? throw new ArgumentNullException(nameof(hotfixRuntime));
    }

    public async ValueTask<FeatureMessageReply> HandleAsync(
        FeatureMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var handlers = _hotfixRuntime.Current.Services.GetService(typeof(IEnumerable<IFeatureMessageHandler>))
            as IEnumerable<IFeatureMessageHandler>;
        if (handlers is null)
        {
            return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
        }

        foreach (var handler in handlers)
        {
            var reply = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            if (reply.Status != ClusterSendStatus.FeatureNotFound)
            {
                return reply;
            }
        }

        return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
    }
}
