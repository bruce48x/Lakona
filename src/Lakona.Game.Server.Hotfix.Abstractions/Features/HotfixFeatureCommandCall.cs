using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixFeatureCommandCall<TRequest> : IHotfixCallContext
{
    public HotfixFeatureCommandCall(
        TRequest request,
        string featureName,
        FeatureCommandId commandId,
        string correlationId,
        NodeId sourceNode,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken,
        IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(services);

        Request = request;
        FeatureName = featureName;
        CommandId = commandId;
        CorrelationId = correlationId;
        SourceNode = sourceNode;
        ExpiresAt = expiresAt;
        CancellationToken = cancellationToken;
        Services = services;
    }

    public TRequest Request { get; }
    public string FeatureName { get; }
    public FeatureCommandId CommandId { get; }
    public string CorrelationId { get; }
    public NodeId SourceNode { get; }
    public DateTimeOffset ExpiresAt { get; }
    public CancellationToken CancellationToken { get; }
    public IServiceProvider Services { get; }
}
