using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Features;

public sealed class FeatureCommandClient : IFeatureCommandClient
{
    private readonly IFeatureMessageBus _messages;
    private readonly IFeatureMessageSerializer _serializer;

    public FeatureCommandClient(
        IFeatureMessageBus messages,
        IFeatureMessageSerializer serializer)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async ValueTask<TReply> SendAsync<TRequest, TReply>(
        string featureName,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        var commandId = GetCommandId<TRequest>();
        var reply = await _messages.SendToFeatureAsync<TRequest, TReply>(
            new FeatureName(featureName),
            commandId.ToString(),
            request,
            cancellationToken).ConfigureAwait(false);

        return reply.GetPayload<TReply>(_serializer);
    }

    public async ValueTask<TReply> SendToNodeAsync<TRequest, TReply>(
        ClusterNodeDescriptor target,
        string featureName,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        var commandId = GetCommandId<TRequest>();
        var reply = await _messages.SendToNodeAsync<TRequest, TReply>(
            target,
            new FeatureName(featureName),
            commandId.ToString(),
            request,
            cancellationToken).ConfigureAwait(false);

        return reply.GetPayload<TReply>(_serializer);
    }

    private static FeatureCommandId GetCommandId<TRequest>()
    {
        var attribute = typeof(TRequest).GetCustomAttributes(typeof(FeatureCommandAttribute), inherit: false)
            .Cast<FeatureCommandAttribute>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Feature command request type '{typeof(TRequest).FullName}' must declare FeatureCommandAttribute.");

        return FeatureCommandId.From(attribute.Id);
    }
}
