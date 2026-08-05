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
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var directory = new InMemoryGameSessionRegistry(
            new Lakona.Game.Server.Configuration.LakonaGameHostingOptions
            {
                Sessions = new Lakona.Game.Server.Configuration.LakonaSessionHostingOptions
                {
                    ResumeWindow = TimeSpan.FromMilliseconds(1)
                }
            },
            time);
        var service = new GameSessionCleanupHostedService(
            directory,
            new InMemoryGameSessionResumeTicketStore(),
            new SessionCleanupOptions(),
            [],
            NullLogger<GameSessionCleanupHostedService>.Instance,
            time);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await directory.MarkSessionDisconnectedAsync(session, "connection-a", TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(1));

        await service.CleanupOnceAsync(TestContext.Current.CancellationToken);

        var decision = await directory.TryResumeAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(SessionResumeStatus.StateLost, decision.Status);
    }

    [Fact]
    public async Task CleanupOncePublishesSessionExpiredAndContainsHandlerFailures()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var directory = new InMemoryGameSessionRegistry(
            new Lakona.Game.Server.Configuration.LakonaGameHostingOptions
            {
                Sessions = new Lakona.Game.Server.Configuration.LakonaSessionHostingOptions
                {
                    ResumeWindow = TimeSpan.FromMilliseconds(1)
                }
            },
            time);
        var throwingHandler = new ThrowingLifecycleHandler();
        var recordingHandler = new RecordingLifecycleHandler();
        var service = new GameSessionCleanupHostedService(
            directory,
            new InMemoryGameSessionResumeTicketStore(),
            new SessionCleanupOptions(),
            new IGameSessionLifecycleHandler[] { throwingHandler, recordingHandler },
            NullLogger<GameSessionCleanupHostedService>.Instance,
            time);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await directory.MarkSessionDisconnectedAsync(session, "connection-a", TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(1));

        await service.CleanupOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal("connection-a", recordingHandler.ExpiredConnectionId);
        Assert.True(throwingHandler.WasCalled);
    }

    [Fact]
    public async Task CleanupOnceRemovesRetainedTerminationAndTicketWithoutPublishingSessionExpired()
    {
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        var directory = new InMemoryGameSessionRegistry(
            new Lakona.Game.Server.Configuration.LakonaGameHostingOptions
            {
                Sessions = new Lakona.Game.Server.Configuration.LakonaSessionHostingOptions
                {
                    ResumeWindow = TimeSpan.FromSeconds(60),
                },
            },
            time);
        var tickets = new InMemoryGameSessionResumeTicketStore();
        var handler = new RecordingLifecycleHandler();
        var service = new GameSessionCleanupHostedService(
            directory,
            tickets,
            new SessionCleanupOptions(),
            [handler],
            NullLogger<GameSessionCleanupHostedService>.Instance,
            time);
        var session = await directory.StartNewSessionAsync(
            "player-a",
            TestContext.Current.CancellationToken);
        var ticket = await tickets.IssueAsync(
            session,
            "control",
            TestContext.Current.CancellationToken);
        await directory.MarkSessionTerminatedAsync(
            session,
            new SessionTerminationNotice(SessionTerminationReason.Policy),
            keepForResume: true,
            TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(60));
        await service.CleanupOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, directory.GetDiagnosticsSnapshot().TotalSessions);
        Assert.Null(await tickets.ResolveAsync(
            ticket,
            "control",
            TestContext.Current.CancellationToken));
        Assert.Null(handler.ExpiredConnectionId);
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
    public void AddLakonaGameServerAlwaysRegistersBoundedSessionCleanup()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddLakonaGameServer(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is GameSessionCleanupHostedService);
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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
