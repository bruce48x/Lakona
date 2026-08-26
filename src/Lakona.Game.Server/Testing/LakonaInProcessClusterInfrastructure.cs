using System.ComponentModel;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Testing;

/// <summary>
/// The transport boundary used by the separately packaged Lakona in-process
/// cluster test host.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ILakonaInProcessClusterTransport
{
    string Scheme { get; }

    ValueTask<ITransport> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    ValueTask<IRpcConnectionAcceptor> ListenAsync(
        string endpoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the shared framework infrastructure used by one in-process test
/// cluster without exposing Membership or cluster-RPC implementation types.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LakonaInProcessClusterInfrastructure
{
    private readonly InMemoryMembershipTable membershipTable = new();

    public void ConfigureNode(
        IServiceCollection services,
        ILakonaInProcessClusterTransport transport)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(transport);

        services.RemoveAll<IMembershipTable>();
        services.AddSingleton<IMembershipTable>(membershipTable);
        services.RemoveAll<ClusterRpcChannel>();
        services.AddSingleton(new ClusterRpcChannel(
            new ClusterTransportAdapter(transport),
            new MemoryPackRpcSerializer(),
            ClusterProtocol.Identifier));
    }

    private sealed class ClusterTransportAdapter(
        ILakonaInProcessClusterTransport transport) : IClusterRpcTransport
    {
        public string Scheme => transport.Scheme;

        public ValueTask<ITransport> ConnectAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) =>
            transport.ConnectAsync(Format(endpoint), cancellationToken);

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) =>
            transport.ListenAsync(Format(endpoint), cancellationToken);

        private static string Format(ClusterEndpoint endpoint) =>
            $"{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}{endpoint.Path}";
    }
}
