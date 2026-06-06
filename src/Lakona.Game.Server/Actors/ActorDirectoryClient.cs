using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ActorDirectoryClient : IActorDirectory
{
    private readonly object _gate = new();
    private readonly IClusterNodeDiscovery _nodeDiscovery;
    private readonly IActorDirectoryHostClient _hostClient;
    private NodeId? _directoryNode;

    public ActorDirectoryClient(
        IClusterNodeDiscovery nodeDiscovery,
        IActorDirectoryHostClient hostClient)
    {
        _nodeDiscovery = nodeDiscovery ?? throw new ArgumentNullException(nameof(nodeDiscovery));
        _hostClient = hostClient ?? throw new ArgumentNullException(nameof(hostClient));
    }

    public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRediscoveryAsync(
            host => _hostClient.ResolveAsync(host, actorId, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRediscoveryAsync(
            host => _hostClient.RegisterAsync(host, actorId, node, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
        ActorId actorId,
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRediscoveryAsync(
            host => _hostClient.UnregisterAsync(host, actorId, node, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<T> ExecuteWithRediscoveryAsync<T>(
        Func<NodeId, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        var host = await GetDirectoryNodeAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(host).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
            catch
            {
                ClearDirectoryNode(host);
                var refreshedHost = await GetDirectoryNodeAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
                try
            {
                return await operation(refreshedHost).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
                catch (Exception secondFailure)
                {
                    ClearDirectoryNode(refreshedHost);
                    throw new ActorDirectoryUnavailableException(
                        "Actor directory host is unavailable.",
                        secondFailure);
                }
            }
        }

    private async ValueTask<NodeId> GetDirectoryNodeAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh)
        {
            lock (_gate)
            {
                if (_directoryNode is { } cached)
                {
                    return cached;
                }
            }
        }

        var discovered = await _nodeDiscovery.AnyAsync(
            ActorDirectoryFeatures.ActorDirectory,
            cancellationToken).ConfigureAwait(false);

        if (discovered is not { } node)
        {
            throw new ActorDirectoryUnavailableException("Actor directory host was not found.");
        }

        lock (_gate)
        {
            _directoryNode = node;
        }

        return node;
    }

    private void ClearDirectoryNode(NodeId expected)
    {
        lock (_gate)
        {
            if (_directoryNode == expected)
            {
                _directoryNode = null;
            }
        }
    }
}
