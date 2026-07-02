using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Provides arguments to a hotfix feature command handler.
/// </summary>
/// <typeparam name="TRequest">The command request type.</typeparam>
public sealed class HotfixFeatureCommandCall<TRequest> : IHotfixCallContext
{
    /// <summary>
    /// Initializes a new feature command call.
    /// </summary>
    /// <param name="request">The command request payload.</param>
    /// <param name="featureName">The target feature name.</param>
    /// <param name="commandId">The feature command id.</param>
    /// <param name="correlationId">The cluster correlation id.</param>
    /// <param name="sourceNode">The node that sent the command.</param>
    /// <param name="expiresAt">The UTC deadline after which the command should not be processed.</param>
    /// <param name="cancellationToken">The handler cancellation token.</param>
    /// <param name="services">The current hotfix service provider.</param>
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

    /// <summary>
    /// Gets the command request payload.
    /// </summary>
    public TRequest Request { get; }

    /// <summary>
    /// Gets the target feature name.
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Gets the feature command id.
    /// </summary>
    public FeatureCommandId CommandId { get; }

    /// <summary>
    /// Gets the cluster correlation id.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Gets the node that sent the command.
    /// </summary>
    public NodeId SourceNode { get; }

    /// <summary>
    /// Gets the UTC deadline after which the command should not be processed.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets the handler cancellation token.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the current hotfix service provider.
    /// </summary>
    public IServiceProvider Services { get; }
}
