using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Hosting;

public sealed class SeededRouteDirectoryClient : IRouteDirectory
{
    private readonly IClusterClientFactory _clientFactory;
    private readonly RouteLocation _directory;

    public SeededRouteDirectoryClient(
        IClusterClientFactory clientFactory,
        string endpoint)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _directory = new RouteLocation(
            new RouteKey("cluster-directory:routes"),
            new NodeId("cluster-directory-seed"),
            new NodeEndpoint(endpoint),
            DateTimeOffset.MaxValue);
    }

    public async ValueTask<RouteRegistrationStatus> RegisterAsync(
        RouteLocation location,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new RouteDirectoryClient(client)
            .RegisterAsync(location, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<RouteLocation?> ResolveAsync(
        RouteKey route,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new RouteDirectoryClient(client)
            .ResolveAsync(route, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<RouteUnregisterStatus> UnregisterAsync(
        RouteKey route,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new RouteDirectoryClient(client)
            .UnregisterAsync(route, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
        RouteLocation expectedLocation,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new RouteDirectoryClient(client)
            .RefreshLeaseAsync(expectedLocation, expiresAt, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<int> ExpireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new RouteDirectoryClient(client)
            .ExpireAsync(now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<int> ClearByNodeAsync(
        NodeId node,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new RouteDirectoryClient(client)
            .ClearByNodeAsync(node, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<int> ClearByNodeEpochAsync(
        NodeId node,
        long nodeEpoch,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        return await new RouteDirectoryClient(client)
            .ClearByNodeEpochAsync(node, nodeEpoch, cancellationToken)
            .ConfigureAwait(false);
    }

    private ValueTask<Lakona.Rpc.Core.IRpcClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        return _clientFactory.GetClientAsync(_directory, cancellationToken);
    }
}
