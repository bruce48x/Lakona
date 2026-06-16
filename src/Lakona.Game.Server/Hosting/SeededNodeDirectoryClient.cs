using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Hosting;

public sealed class SeededNodeDirectoryClient : INodeDirectory
{
    private readonly IClusterClientFactory _clientFactory;
    private readonly RouteLocation _directory;

    public SeededNodeDirectoryClient(
        IClusterClientFactory clientFactory,
        string endpoint)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _directory = CreateDirectoryLocation(endpoint);
    }

    public async ValueTask<NodeRegistrationResult> RegisterAsync(
        NodeRegistration registration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new NodeDirectoryClient(client)
            .RegisterAsync(registration, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
        string clusterName,
        NodeId node,
        long nodeEpoch,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new NodeDirectoryClient(client)
            .HeartbeatAsync(clusterName, node, nodeEpoch, leaseExpiresAt, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
        string clusterName,
        NodeId node,
        long nodeEpoch,
        NodeState state,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new NodeDirectoryClient(client)
            .UpdateStateAsync(clusterName, node, nodeEpoch, state, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NodeRecord?> ResolveAsync(
        string clusterName,
        NodeId node,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new NodeDirectoryClient(client)
            .ResolveAsync(clusterName, node, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
        NodeDirectoryQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new NodeDirectoryClient(client)
            .QueryAsync(query, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<int> ExpireAsync(
        string clusterName,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new NodeDirectoryClient(client)
            .ExpireAsync(clusterName, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private ValueTask<Lakona.Rpc.Core.IRpcClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        return _clientFactory.GetClientAsync(_directory, cancellationToken);
    }

    private static RouteLocation CreateDirectoryLocation(string endpoint)
    {
        return new RouteLocation(
            new RouteKey("cluster-directory:nodes"),
            new NodeId("cluster-directory-seed"),
            new NodeEndpoint(endpoint),
            DateTimeOffset.MaxValue);
    }
}
