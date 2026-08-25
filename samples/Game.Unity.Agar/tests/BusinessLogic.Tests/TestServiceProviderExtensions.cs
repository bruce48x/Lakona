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
            var runtime = provider.GetService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>();
            var local = provider.GetService<IClusterMembership>()?.Current.Members
                .SingleOrDefault(member => member.Reference.Node.Value == runtime?.Node.Id);
            if (local is not null)
            {
                provider.GetService<Lakona.Game.Server.Actors.LocalActorNodeIdentity>()?.Observe(local.Reference);
            }
            provider.GetService<DistributedWorkAdmissionGate>()?.Open();
            return provider;
        }

        try
        {
            membership.StartAsync(cancellationToken).GetAwaiter().GetResult();
            var gate = provider.GetRequiredService<DistributedWorkAdmissionGate>();
            gate.Open();
            membership.RefreshDescriptorAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
            return provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    public static async Task StartClusterMembershipAsync(
        this IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        var membership = provider.GetRequiredService<MembershipTableHostedService>();
        await membership.StartAsync(cancellationToken).ConfigureAwait(false);
        provider.GetRequiredService<DistributedWorkAdmissionGate>().Open();
        await membership.RefreshDescriptorAsync(cancellationToken).ConfigureAwait(false);
    }
}
