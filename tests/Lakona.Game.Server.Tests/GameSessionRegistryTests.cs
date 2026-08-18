using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionRegistryTests
{
    [Fact]
    public async Task Prepared_binding_is_invisible_until_committed()
    {
        var registry = new InMemoryGameSessionRegistry();
        var session = await registry.StartNewSessionAsync(
            "player-a",
            TestContext.Current.CancellationToken);

        var binding = await registry.PrepareSessionBindingAsync(
            session,
            "connection-a",
            TestContext.Current.CancellationToken);

        Assert.NotNull(binding.SessionBecameActive);
        Assert.Null(await registry.GetCurrentSessionAsync(
            "connection-a",
            TestContext.Current.CancellationToken));
        Assert.Null(await registry.GetConnectionIdAsync(
            session,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, registry.GetDiagnosticsSnapshot().ActiveSessions);

        await registry.CommitSessionBindingAsync(
            session,
            "connection-a",
            TestContext.Current.CancellationToken);

        Assert.Equal(session, await registry.GetCurrentSessionAsync(
            "connection-a",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rolling_back_prepared_binding_restores_disconnected_session()
    {
        var registry = new InMemoryGameSessionRegistry();
        var session = await registry.StartNewSessionAsync(
            "player-a",
            TestContext.Current.CancellationToken);
        await registry.BindSessionAsync(
            session,
            "connection-a",
            TestContext.Current.CancellationToken);
        await registry.MarkSessionDisconnectedAsync(
            session,
            "connection-a",
            TestContext.Current.CancellationToken);

        await registry.PrepareSessionBindingAsync(
            session,
            "connection-b",
            TestContext.Current.CancellationToken);
        await registry.RollbackSessionBindingAsync(
            session,
            "connection-b",
            TestContext.Current.CancellationToken);

        Assert.Null(await registry.GetCurrentSessionAsync(
            "connection-b",
            TestContext.Current.CancellationToken));
        Assert.Equal(
            SessionResumeStatus.Resumed,
            (await registry.TryResumeAsync(
                session,
                TestContext.Current.CancellationToken)).Status);
        var diagnostics = registry.GetDiagnosticsSnapshot();
        Assert.Equal(1, diagnostics.TotalSessions);
        Assert.Equal(0, diagnostics.ActiveSessions);
        Assert.Equal(1, diagnostics.DisconnectedSessions);
    }

    [Fact]
    public async Task Connection_rebind_does_not_clear_resumed_session_replay_barrier()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.SetReliablePushPolicyAsync(session, true, TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(session, "connection-b", TestContext.Current.CancellationToken);
        Assert.True(await directory.IsReliableReplayPendingAsync(session, TestContext.Current.CancellationToken));

        await directory.BindSessionAsync(session, "connection-b", TestContext.Current.CancellationToken);
        Assert.True(await directory.IsReliableReplayPendingAsync(session, TestContext.Current.CancellationToken));

        await directory.MarkReliableReplayReadyAsync(session, TestContext.Current.CancellationToken);
        Assert.False(await directory.IsReliableReplayPendingAsync(session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resume_window_is_enforced_at_the_exact_deadline()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero));
        var directory = new InMemoryGameSessionRegistry(
            new Lakona.Game.Server.Configuration.LakonaGameHostingOptions
            {
                Sessions = new Lakona.Game.Server.Configuration.LakonaSessionHostingOptions
                {
                    ResumeWindow = TimeSpan.FromSeconds(60),
                },
            },
            time);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(SessionResumeStatus.Resumed, (await directory.TryResumeAsync(session, TestContext.Current.CancellationToken)).Status);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SessionResumeStatus.StateLost, (await directory.TryResumeAsync(session, TestContext.Current.CancellationToken)).Status);

        var expired = await directory.ExpireSessionsAsync(
            time.GetUtcNow(),
            TestContext.Current.CancellationToken);
        Assert.Contains(expired, expiration => expiration.Session == session);
    }

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
    public async Task BindingSecondActiveSessionToSameConnectionIsRejected()
    {
        var directory = new InMemoryGameSessionRegistry();
        var first = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var second = await directory.StartNewSessionAsync("player-b", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(first, "connection-a", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => directory
            .BindSessionAsync(second, "connection-a", TestContext.Current.CancellationToken)
            .AsTask());
    }

    [Fact]
    public async Task MarkConnectionDisconnectedReturnsOneConnectionSnapshot()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);

        var disconnected = await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        Assert.NotNull(disconnected);
        Assert.Equal(session, disconnected.Session);
        Assert.Equal("connection-a", disconnected.ConnectionId);
    }

    [Fact]
    public async Task ExpireSessionsReturnsDisconnectedSessionAfterItsDeadline()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        var expired = await directory.ExpireSessionsAsync(
            DateTimeOffset.UtcNow.AddSeconds(61),
            TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(expired);
        Assert.Equal(session, snapshot.Session);
        Assert.Equal("connection-a", snapshot.ConnectionId);
    }

    [Fact]
    public async Task StaleConnectionIdCannotDetachNewerBinding()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "old", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "new", TestContext.Current.CancellationToken);
        await directory.MarkSessionDisconnectedAsync(session, "old", TestContext.Current.CancellationToken);

        Assert.Equal(session, await directory.GetCurrentSessionAsync("new", TestContext.Current.CancellationToken));
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
    public async Task Retained_termination_expires_at_the_resume_deadline()
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
        var session = await directory.StartNewSessionAsync(
            "player-a",
            TestContext.Current.CancellationToken);

        await directory.MarkSessionTerminatedAsync(
            session,
            new SessionTerminationNotice(SessionTerminationReason.Policy),
            keepForResume: true,
            TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(
            SessionResumeStatus.Terminated,
            (await directory.TryResumeAsync(session, TestContext.Current.CancellationToken)).Status);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(
            SessionResumeStatus.StateLost,
            (await directory.TryResumeAsync(session, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Termination_without_resume_retention_removes_the_session_immediately()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync(
            "player-a",
            TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(
            session,
            "connection-a",
            TestContext.Current.CancellationToken);

        await directory.MarkSessionTerminatedAsync(
            session,
            new SessionTerminationNotice(SessionTerminationReason.Policy),
            keepForResume: false,
            TestContext.Current.CancellationToken);

        var decision = await directory.TryResumeAsync(
            session,
            TestContext.Current.CancellationToken);
        var heartbeat = await directory.RecordHeartbeatAsync(
            "connection-a",
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);
        var diagnostics = directory.GetDiagnosticsSnapshot();

        Assert.Equal(SessionResumeStatus.StateLost, decision.Status);
        Assert.Equal(GameSessionHeartbeatStatus.ConnectionOnly, heartbeat.Status);
        Assert.Equal(0, diagnostics.TotalSessions);
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
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);

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
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);

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
    public async Task Session_items_can_be_set_read_overwritten_and_removed()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);
        Assert.Equal("room-a", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());

        await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-b"), TestContext.Current.CancellationToken);
        Assert.Equal("room-b", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());

        await directory.RemoveSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken);
        Assert.Null(await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Session_items_use_ordinal_case_sensitive_keys()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("lower"), TestContext.Current.CancellationToken);
        await directory.SetSessionItemAsync(session, "RoomId", GameSessionItemValue.FromString("upper"), TestContext.Current.CancellationToken);

        Assert.Equal("lower", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
        Assert.Equal("upper", (await directory.GetSessionItemAsync(session, "RoomId", TestContext.Current.CancellationToken))?.GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task Session_item_keys_reject_empty_or_whitespace_values(string key)
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => directory
            .SetSessionItemAsync(session, key, GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken)
            .AsTask());
    }

    [Fact]
    public async Task Default_session_item_value_is_rejected()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => directory
            .SetSessionItemAsync(session, "roomId", default, TestContext.Current.CancellationToken)
            .AsTask());
    }

    [Fact]
    public async Task Session_item_snapshots_are_immutable_after_later_mutation()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);

        var snapshot = await directory.GetSessionItemsAsync(session, TestContext.Current.CancellationToken);
        await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-b"), TestContext.Current.CancellationToken);

        Assert.Equal("room-a", snapshot.GetString("roomId"));
        Assert.Equal("room-b", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
    }

    [Fact]
    public async Task Unchanged_session_item_snapshot_reads_reuse_the_published_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", cancellationToken);
        await directory.SetSessionItemAsync(
            session,
            "roomId",
            GameSessionItemValue.FromString("room-a"),
            cancellationToken);

        var first = await directory.GetSessionItemsAsync(session, cancellationToken);

        // Keep the measured region synchronous. Measuring through await would
        // mix async continuation behavior into the thread-local allocation
        // counter instead of measuring only the completed ValueTask hot path.
        _ = ReadSessionItemsSynchronously(directory, session, first, cancellationToken, 1_000);

        var measuredThreadId = Environment.CurrentManagedThreadId;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var current = ReadSessionItemsSynchronously(directory, session, first, cancellationToken, 10_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(measuredThreadId, Environment.CurrentManagedThreadId);
        Assert.Same(first, current);
        Assert.Equal(0, allocated);
    }

    private static GameSessionItems ReadSessionItemsSynchronously(
        InMemoryGameSessionRegistry directory,
        GameSessionKey session,
        GameSessionItems initial,
        CancellationToken cancellationToken,
        int count)
    {
        var current = initial;
        for (var i = 0; i < count; i++)
        {
            current = directory
                .GetSessionItemsAsync(session, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }

        return current;
    }

    [Fact]
    public async Task Session_item_keys_reject_overlong_values()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var key = new string('k', 129);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => directory
            .SetSessionItemAsync(session, key, GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken)
            .AsTask());
    }

    [Fact]
    public async Task Session_items_survive_disconnect_and_resume()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);
        await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);

        await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);
        var decision = await directory.TryResumeAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.Resumed, decision.Status);
        Assert.Equal("room-a", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
    }

    [Fact]
    public async Task Session_items_are_inaccessible_after_termination_even_when_terminal_resume_state_is_retained()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);

        await directory.MarkSessionTerminatedAsync(
            session,
            new SessionTerminationNotice(SessionTerminationReason.Policy, "removed"),
            keepForResume: true,
            TestContext.Current.CancellationToken);

        Assert.Null(await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken));
        Assert.Equal(0, (await directory.GetSessionItemsAsync(session, TestContext.Current.CancellationToken)).Count);
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

        await directory.BindSessionAsync(active, activeConnection, TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(disconnected, disconnectedConnection, TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(retained, retainedConnection, TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(dropped, droppedConnection, TestContext.Current.CancellationToken);
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

        Assert.Equal(3, snapshot.TotalSessions);
        Assert.Equal(1, snapshot.ActiveSessions);
        Assert.Equal(1, snapshot.ActiveConnections);
        Assert.Equal(1, snapshot.DisconnectedSessions);
        Assert.Equal(1, snapshot.TerminatedSessions);
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
        await directory.BindSessionAsync(session, "connection-a", TestContext.Current.CancellationToken);
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
    public void Session_item_types_do_not_leak_to_client_or_shared_contract_projects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "Lakona.Game.Abstractions"),
            Path.Combine(repositoryRoot, "src", "Lakona.Game.Client"),
            Path.Combine(repositoryRoot, "src", "Lakona.Rpc.Client"),
        };

        var matches = sourceRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, LineNumber = index + 1, Line = line })
                .Where(match => match.Line.Contains("GameSessionItem", StringComparison.Ordinal)))
            .Select(match => $"{Path.GetRelativePath(repositoryRoot, match.Path)}:{match.LineNumber}: {match.Line.Trim()}")
            .ToArray();

        Assert.True(matches.Length == 0, "GameSessionItem types must remain server-only:" + Environment.NewLine + string.Join(Environment.NewLine, matches));
    }

    [Fact]
    public void AddSessionsRegistersDirectory()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServerSessions();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IGameSessionRegistry>());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
