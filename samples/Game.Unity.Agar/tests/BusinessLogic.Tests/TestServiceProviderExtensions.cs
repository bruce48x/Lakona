using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Agar.Unity.Tests;

internal static class TestServiceProviderExtensions
{
    public static ServiceProvider BuildReadyServiceProvider(
        this IServiceCollection services,
        CancellationToken cancellationToken = default)
    {
        var provider = services.BuildServiceProvider();
        var membership = provider.GetService<MembershipTableHostedService>();
        if (membership is null || provider.GetService<IMembershipTable>() is not InMemoryMembershipTable)
        {
            return provider;
        }

        if (!ReferenceEquals(
            provider.GetService<IClusterMembership>(),
            provider.GetService<ClusterMembershipState>()))
        {
            provider.GetService<DistributedWorkAdmissionGate>()?.Open();
            return provider;
        }

        try
        {
            membership.StartAsync(cancellationToken).GetAwaiter().GetResult();
            return provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    public static Task StartClusterMembershipAsync(
        this IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        var membership = provider.GetRequiredService<MembershipTableHostedService>();
        return membership.StartAsync(cancellationToken);
    }
}
