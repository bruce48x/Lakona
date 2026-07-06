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
    public async Task Current_session_lookup_returns_active_session_for_connection()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);

        var current = await directory.GetCurrentSessionAsync("connection-a", TestContext.Current.CancellationToken);

        Assert.Equal(session, current);
    }

    [Fact]
    public async Task Current_session_lookup_returns_null_for_unbound_connection()
    {
        var directory = new InMemoryGameSessionRegistry();

        var current = await directory.GetCurrentSessionAsync("connection-a", TestContext.Current.CancellationToken);

        Assert.Null(current);
    }

    [Fact]
    public async Task Session_diagnostics_snapshot_reports_counts_without_session_or_connection_ids()
    {
        var directory = new InMemoryGameSessionRegistry();
        var playerSecret = "player-secret";
        var connectionSecret = "connection-secret";
        var session = await directory.StartNewSessionAsync(playerSecret, TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(
            session,
            connectionSecret,
            new LoginCallback("login"),
            TestContext.Current.CancellationToken);

        var snapshot = directory.GetDiagnosticsSnapshot();
        var text = snapshot.ToString();

        Assert.Equal(1, snapshot.ActiveSessions);
        Assert.Equal(1, snapshot.ActiveConnections);
        Assert.DoesNotContain(playerSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(session.SessionId, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_diagnostics_snapshot_counts_mixed_states_without_secrets()
    {
        var directory = new InMemoryGameSessionRegistry();
        var activeOwner = "active-player-secret";
        var disconnectedOwner = "disconnected-player-secret";
        var retainedOwner = "retained-player-secret";
        var droppedOwner = "dropped-player-secret";
        var activeConnection = "active-connection-secret";
        var disconnectedConnection = "disconnected-connection-secret";
        var retainedConnection = "retained-connection-secret";
        var droppedConnection = "dropped-connection-secret";
        var active = await directory.StartNewSessionAsync(activeOwner, TestContext.Current.CancellationToken);
        var disconnected = await directory.StartNewSessionAsync(disconnectedOwner, TestContext.Current.CancellationToken);
        var retained = await directory.StartNewSessionAsync(retainedOwner, TestContext.Current.CancellationToken);
        var dropped = await directory.StartNewSessionAsync(droppedOwner, TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(active, activeConnection, new LoginCallback("active"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(disconnected, disconnectedConnection, new LoginCallback("disconnected"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(retained, retainedConnection, new LoginCallback("retained"), TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(dropped, droppedConnection, new LoginCallback("dropped"), TestContext.Current.CancellationToken);
        await directory.MarkConnectionDisconnectedAsync(disconnectedConnection, TestContext.Current.CancellationToken);
        await directory.MarkSessionTerminatedAsync(
            retained,
            new SessionTerminationNotice(SessionTerminationReason.Policy, "retained"),
            keepForResume: true,
            TestContext.Current.CancellationToken);
        await directory.MarkSessionTerminatedAsync(
            dropped,
            new SessionTerminationNotice(SessionTerminationReason.Policy, "dropped"),
            keepForResume: false,
            TestContext.Current.CancellationToken);

        var snapshot = directory.GetDiagnosticsSnapshot();
        var text = snapshot.ToString();

        Assert.Equal(4, snapshot.TotalSessions);
        Assert.Equal(1, snapshot.ActiveSessions);
        Assert.Equal(1, snapshot.ActiveConnections);
        Assert.Equal(1, snapshot.DisconnectedSessions);
        Assert.Equal(2, snapshot.TerminatedSessions);
        Assert.Equal(3, snapshot.ResumableSessions);
        Assert.DoesNotContain(activeOwner, text, StringComparison.Ordinal);
        Assert.DoesNotContain(disconnectedOwner, text, StringComparison.Ordinal);
        Assert.DoesNotContain(retainedOwner, text, StringComparison.Ordinal);
        Assert.DoesNotContain(droppedOwner, text, StringComparison.Ordinal);
        Assert.DoesNotContain(activeConnection, text, StringComparison.Ordinal);
        Assert.DoesNotContain(disconnectedConnection, text, StringComparison.Ordinal);
        Assert.DoesNotContain(retainedConnection, text, StringComparison.Ordinal);
        Assert.DoesNotContain(droppedConnection, text, StringComparison.Ordinal);
        Assert.DoesNotContain(active.SessionId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(disconnected.SessionId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(retained.SessionId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(dropped.SessionId, text, StringComparison.Ordinal);
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
    public void Session_item_value_preserves_scalar_kinds()
    {
        var text = GameSessionItemValue.FromString("room-a");
        var number = GameSessionItemValue.FromInt64(42);
        var flag = GameSessionItemValue.FromBoolean(true);

        Assert.Equal(GameSessionItemKind.String, text.Kind);
        Assert.Equal("room-a", text.GetString());
        Assert.Equal(GameSessionItemKind.Int64, number.Kind);
        Assert.Equal(42, number.GetInt64());
        Assert.Equal(GameSessionItemKind.Boolean, flag.Kind);
        Assert.True(flag.GetBoolean());
    }

    [Fact]
    public void Empty_session_items_snapshot_returns_missing_values()
    {
        Assert.False(GameSessionItems.Empty.TryGetValue("roomId", out _));
        Assert.Null(GameSessionItems.Empty.GetString("roomId"));
        Assert.Null(GameSessionItems.Empty.GetInt64("generation"));
        Assert.Null(GameSessionItems.Empty.GetBoolean("ready"));
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
