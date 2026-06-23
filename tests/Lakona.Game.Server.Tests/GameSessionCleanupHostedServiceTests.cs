using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Lakona.Game.Abstractions;
using Lakona.Game.Server;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionCleanupHostedServiceTests
{
    [Fact]
    public async Task CleanupOnceExpiresDisconnectedSessions()
    {
        var directory = new InMemoryGameSessionRegistry();
        var service = new GameSessionCleanupHostedService(
            directory,
            new SessionCleanupOptions
            {
                DisconnectedSessionRetention = TimeSpan.FromMilliseconds(1)
            });
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new Callback(), TestContext.Current.CancellationToken);
        await directory.MarkSessionDisconnectedAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);

        await service.CleanupOnceAsync(TestContext.Current.CancellationToken);

        Assert.Null(await directory.GetCallbackAsync<Callback>(session, TestContext.Current.CancellationToken));
        var decision = await directory.TryResumeAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(SessionResumeStatus.StateLost, decision.Status);
    }

    [Fact]
    public async Task CleanupOncePublishesSessionExpiredAndContainsHandlerFailures()
    {
        var directory = new InMemoryGameSessionRegistry();
        var throwingHandler = new ThrowingLifecycleHandler();
        var recordingHandler = new RecordingLifecycleHandler();
        var service = new GameSessionCleanupHostedService(
            directory,
            new SessionCleanupOptions
            {
                DisconnectedSessionRetention = TimeSpan.FromMilliseconds(1)
            },
            new IGameSessionLifecycleHandler[] { throwingHandler, recordingHandler },
            NullLogger<GameSessionCleanupHostedService>.Instance);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new Callback(), TestContext.Current.CancellationToken);
        await directory.MarkSessionDisconnectedAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);

        await service.CleanupOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal("connection-a", recordingHandler.ExpiredConnectionId);
        Assert.True(throwingHandler.WasCalled);
        Assert.Null(await directory.GetCallbackAsync<Callback>(session, TestContext.Current.CancellationToken));
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

    [Fact]
    public void AddLakonaGameServerWithConfigurationSkipsCleanupHostedServiceWhenDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Sessions:Cleanup:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLakonaGameServer(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<SessionCleanupOptions>().Enabled);
        Assert.DoesNotContain(provider.GetServices<IHostedService>(), service => service is GameSessionCleanupHostedService);
    }

    private sealed class Callback
    {
    }

    private sealed class ThrowingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public bool WasCalled { get; private set; }

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("boom");
        }

        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class RecordingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public string? ExpiredConnectionId { get; private set; }

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            ExpiredConnectionId = context.ConnectionId;
            return default;
        }

        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
