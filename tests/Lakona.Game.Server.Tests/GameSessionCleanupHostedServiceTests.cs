using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task CleanupOncePublishesEndpointExpiredAndContainsHandlerFailures()
    {
        var directory = new InMemoryGameSessionDirectory();
        var throwingHandler = new ThrowingLifecycleHandler();
        var recordingHandler = new RecordingLifecycleHandler();
        var service = new GameSessionCleanupHostedService(
            directory,
            new SessionCleanupOptions
            {
                DisconnectedEndpointRetention = TimeSpan.FromMilliseconds(1)
            },
            new IGameSessionLifecycleHandler[] { throwingHandler, recordingHandler },
            NullLogger<GameSessionCleanupHostedService>.Instance);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var endpoint = new SessionEndpointKey(session, "control");
        await directory.BindEndpointAsync(endpoint, "connection-a", new Callback(), TestContext.Current.CancellationToken);
        await directory.MarkEndpointDisconnectedAsync(endpoint, "connection-a", TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);

        await service.CleanupOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal("connection-a", recordingHandler.ExpiredConnectionId);
        Assert.True(throwingHandler.WasCalled);
        Assert.Null(await directory.GetCallbackAsync<Callback>(endpoint, TestContext.Current.CancellationToken));
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

    private sealed class ThrowingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public bool WasCalled { get; private set; }

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnEndpointBoundAsync(GameEndpointBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnEndpointDisconnectedAsync(GameEndpointBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnEndpointExpiredAsync(GameEndpointBindingContext context, CancellationToken cancellationToken = default)
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

        public ValueTask OnEndpointBoundAsync(GameEndpointBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnEndpointDisconnectedAsync(GameEndpointBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnEndpointExpiredAsync(GameEndpointBindingContext context, CancellationToken cancellationToken = default)
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
