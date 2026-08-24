using Lakona.Game.Cluster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Tests;

internal static class ReadySingleNodeMembership
{
    private static readonly ClusterIncarnationId Cluster =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    public static IServiceCollection UseReadySingleNodeMembership(
        this IServiceCollection services,
        string nodeId = "dev-1")
    {
        var reference = new NodeReference(
            Cluster,
            new NodeId(nodeId),
            new NodeIncarnationId(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var snapshot = new ClusterMembershipSnapshot(
            Cluster,
            new MembershipViewId(1),
            [
                new ClusterMember(
                    reference,
                    ClusterMemberState.Active,
                    new NodeEndpoint("tcp://127.0.0.1:21001"))
            ]);

        services.Replace(ServiceDescriptor.Singleton<IClusterMembership>(
            new FixedMembership(snapshot)));
        return services;
    }

    private sealed class FixedMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; } = current;

        public async ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Current;
        }
    }
}
