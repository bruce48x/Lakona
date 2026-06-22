namespace Lakona.Game.Server.Features;

public interface IFeatureCommandClient
{
    ValueTask<TReply> SendAsync<TRequest, TReply>(
        string featureName,
        TRequest request,
        CancellationToken cancellationToken = default);
}
