using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionRegistryTests
{
    [Fact]
    public async Task StartingSecondSessionForSameOwnerLeavesBothSessionsResumable()
    {
        var directory = new InMemoryGameSessionRegistry();
        var first = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var second = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        var firstDecision = await directory.TryResumeAsync(first, TestContext.Current.CancellationToken);
        var secondDecision = await directory.TryResumeAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.Resumed, firstDecision.Status);
        Assert.Equal(SessionResumeStatus.Resumed, secondDecision.Status);
    }

    [Fact]
    public async Task MultipleCallbackContractsShareOneSessionWithoutOverwritingEachOther()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var login = new LoginCallback("login");
        var chat = new ChatCallback("chat");

        await directory.BindSessionAsync(session, "connection-a", login, TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", chat, TestContext.Current.CancellationToken);

        Assert.Same(login, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
        Assert.Same(chat, await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RebindingSameCallbackContractOnSameConnectionReplacesOnlyThatContract()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var firstLogin = new LoginCallback("first-login");
        var secondLogin = new LoginCallback("second-login");
        var chat = new ChatCallback("chat");

        await directory.BindSessionAsync(session, "connection-a", firstLogin, TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", chat, TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", secondLogin, TestContext.Current.CancellationToken);

        Assert.Same(secondLogin, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
        Assert.Same(chat, await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RebindingSameSessionToNewConnectionClearsCallbacksFromOldConnection()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var login = new LoginCallback("login");

        await directory.BindSessionAsync(session, "old-connection", new LoginCallback("old-login"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "old-connection", new ChatCallback("old-chat"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "new-connection", login, TestContext.Current.CancellationToken);

        Assert.Same(login, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
        Assert.Null(await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BindingSecondActiveSessionToSameConnectionIsRejected()
    {
        var directory = new InMemoryGameSessionRegistry();
        var first = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var second = await directory.StartNewSessionAsync("player-b", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(first, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => directory
            .BindSessionAsync(second, "connection-a", new LoginCallback("other"), TestContext.Current.CancellationToken)
            .AsTask());
    }

    [Fact]
    public async Task MarkConnectionDisconnectedReturnsOneSessionSnapshotAndClearsCallbacks()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new ChatCallback("chat"), TestContext.Current.CancellationToken);

        var disconnected = await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        Assert.NotNull(disconnected);
        Assert.Equal(session, disconnected.Session);
        Assert.Equal("connection-a", disconnected.ConnectionId);
        Assert.Equal(2, disconnected.CallbackContractTypes.Count);
        Assert.Contains(typeof(LoginCallback), disconnected.CallbackContractTypes);
        Assert.Contains(typeof(ChatCallback), disconnected.CallbackContractTypes);
        Assert.Null(await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
        Assert.Null(await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExpireDisconnectedSessionsReturnsStaleDisconnectedSessionOnce()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new ChatCallback("chat"), TestContext.Current.CancellationToken);
        await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        var expired = await directory.ExpireDisconnectedSessionsAsync(DateTimeOffset.UtcNow.AddSeconds(1), TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(expired);
        Assert.Equal(session, snapshot.Session);
        Assert.Equal("connection-a", snapshot.ConnectionId);
        Assert.Equal(2, snapshot.CallbackContractTypes.Count);
    }

    [Fact]
    public async Task StaleConnectionIdCannotDetachNewerBinding()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var callback = new LoginCallback("new");

        await directory.BindSessionAsync(session, "old", new LoginCallback("old"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "new", callback, TestContext.Current.CancellationToken);
        await directory.MarkSessionDisconnectedAsync(session, "old", TestContext.Current.CancellationToken);

        Assert.Same(callback, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TerminatedSessionResumesAsTerminated()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var notice = new SessionTerminationNotice(SessionTerminationReason.Policy, "Removed.");

        await directory.MarkSessionTerminatedAsync(
            session,
            notice,
            keepForResume: true,
            TestContext.Current.CancellationToken);

        var decision = await directory.TryResumeAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.Terminated, decision.Status);
        Assert.Same(notice, decision.Termination);
    }

    [Fact]
    public async Task BindingSessionAfterTerminationIsRejected()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var notice = new SessionTerminationNotice(SessionTerminationReason.Policy);

        await directory.MarkSessionTerminatedAsync(
            session,
            notice,
            keepForResume: true,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => directory
            .BindSessionAsync(
                session,
                "connection-a",
                new Callback("control"),
                TestContext.Current.CancellationToken)
            .AsTask());
    }

    [Fact]
    public async Task Heartbeat_for_unbound_connection_is_connection_only()
    {
        var directory = new InMemoryGameSessionRegistry();

        var result = await directory.RecordHeartbeatAsync(
            "connection-a",
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.Equal(GameSessionHeartbeatStatus.ConnectionOnly, result.Status);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task Heartbeat_for_bound_connection_reports_active_session()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);

        var result = await directory.RecordHeartbeatAsync(
            "connection-a",
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.Equal(GameSessionHeartbeatStatus.ActiveSession, result.Status);
        Assert.Equal(session, result.Session);
    }

    [Fact]
    public async Task Heartbeat_for_connection_that_was_terminated_reports_terminated()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
        await directory.MarkSessionTerminatedAsync(
            session,
            new SessionTerminationNotice(SessionTerminationReason.Policy, "removed"),
            keepForResume: true,
            TestContext.Current.CancellationToken);

        var result = await directory.RecordHeartbeatAsync(
            "connection-a",
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.Equal(GameSessionHeartbeatStatus.Terminated, result.Status);
        Assert.Equal(session, result.Session);
        Assert.Equal("removed", result.Termination?.Message);
    }

    [Fact]
    public void AddSessionsRegistersDirectory()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServerSessions();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IGameSessionRegistry>());
    }

    private sealed class Callback
    {
        public Callback(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class LoginCallback
    {
        public LoginCallback(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class ChatCallback
    {
        public ChatCallback(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
