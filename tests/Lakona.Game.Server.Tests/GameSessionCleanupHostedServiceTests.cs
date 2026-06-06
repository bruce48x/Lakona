using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionCleanupHostedServiceTests
{
    [Fact]
    public async Task CleanupOnceExpiresDisconnectedEndpoints()
    {
        var directory = new InMemoryGameSessionDirectory();
        var service = new GameSessionCleanupHostedService(
            directory,
            new SessionCleanupOptions
            {
                DisconnectedEndpointRetention = TimeSpan.FromMilliseconds(1)
            });
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var endpoint = new SessionEndpointKey(session, "control");
        await directory.BindEndpointAsync(endpoint, "connection-a", new Callback(), TestContext.Current.CancellationToken);
        await directory.MarkEndpointDisconnectedAsync(endpoint, "connection-a", TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);

        await service.CleanupOnceAsync(TestContext.Current.CancellationToken);

        Assert.Null(await directory.GetCallbackAsync<Callback>(endpoint, TestContext.Current.CancellationToken));
        var decision = await directory.TryResumeAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(SessionResumeStatus.StateLost, decision.Status);
    }

    [Fact]
    public void AddSessionCleanupRegistersHostedServiceAndOptions()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServerSessionCleanup(options => options.Interval = TimeSpan.FromSeconds(5));
        using var provider = services.BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<SessionCleanupOptions>().Interval);
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is GameSessionCleanupHostedService);
    }

    private sealed class Callback
    {
    }
}
