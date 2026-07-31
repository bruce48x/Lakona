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
        var membership = provider.GetService<ReplicatedClusterMembershipHostedService>();
        if (membership is null)
        {
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
        var membership = provider.GetRequiredService<ReplicatedClusterMembershipHostedService>();
        return membership.StartAsync(cancellationToken);
    }
}
