using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class LakonaGameClientCoreTests
{
    [Fact]
    public async Task MainEntryProcessesReliablePushAndAppliesAckOutcome()
    {
        var client = new LakonaGameClientCore();
        var session = "session-a";
        var applied = new List<string>();
        client.StartSession(session);

        var result = await client.ProcessReliablePushAsync(
            ReliablePushSequence.From(1),
            "matched",
            (payload, _) =>
            {
                applied.Add(payload);
                return default;
            },
            (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.StateRefreshRequired()),
            TestContext.Current.CancellationToken);

        Assert.True(result.Decision.ShouldApply);
        Assert.Equal("matched", Assert.Single(applied));
        Assert.Equal(ClientSessionPhase.RefreshRequired, client.Snapshot.Phase);
        Assert.Equal(session, client.Snapshot.SessionId);
        Assert.Equal(1, client.Snapshot.LastReliableSequence);
    }

    [Fact]
    public async Task MainEntryMakesStateLostTerminalUntilNewSession()
    {
        var client = new LakonaGameClientCore();
        var session = "session-a";
        client.StartSession(session);

        await client.ProcessReliablePushAsync(
            ReliablePushSequence.From(1),
            "matched",
            (_, _) => default,
            (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.SessionMismatch()),
            TestContext.Current.CancellationToken);
        client.MarkReconnecting();

        Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.SessionId);

        var next = "session-b";
        client.StartSession(next);

        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        Assert.Equal(next, client.Snapshot.SessionId);
    }

    [Fact]
    public void MainEntryAppliesSessionTerminationNotice()
    {
        var client = new LakonaGameClientCore();
        var notice = new SessionTerminationNotice(SessionTerminationReason.Policy, "Removed.");
        client.StartSession("session-a", lastReliableSequence: 7);

        client.ApplySessionTerminationNotice(notice);

        Assert.Equal(ClientSessionPhase.Terminated, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Equal(0, client.Snapshot.LastReliableSequence);
        Assert.Same(notice, client.Snapshot.Termination);
    }

    [Fact]
    public void ApplyServerHello_disables_reliable_push_ack_when_server_reports_immediate_mode()
    {
        var client = new LakonaGameClientCore();

        client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = false,
                DeliveryMode = "immediate",
                AckRequired = false,
                ReplaySupported = false,
                MaxPending = 0
            }
        });

        Assert.False(client.ReliablePushEnabled);
        Assert.False(client.ReliablePushAckRequired);
    }

    [Fact]
    public void ApplyServerHello_enables_reliable_push_ack_when_server_reports_reliable_mode()
    {
        var client = new LakonaGameClientCore();

        client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = true,
                DeliveryMode = "reliable",
                AckRequired = true,
                ReplaySupported = true,
                MaxPending = 256
            }
        });

        Assert.True(client.ReliablePushEnabled);
        Assert.True(client.ReliablePushAckRequired);
    }

    [Fact]
    public void Core_marks_ready_after_handshake_pipeline_completes()
    {
        var client = new LakonaGameClientCore();

        client.MarkConnecting();
        client.MarkReady();

        Assert.Equal(ClientSessionPhase.Ready, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Null(client.Snapshot.Failure);
    }

    [Fact]
    public void Core_records_connection_failure_and_keeps_api_instance_terminal()
    {
        var client = new LakonaGameClientCore();
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");

        client.MarkConnecting();
        client.MarkConnectionFailed(failure);
        client.MarkReady();

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
    }

    [Fact]
    public void Core_start_session_does_not_promote_connection_failed_client()
    {
        var client = new LakonaGameClientCore();
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");

        client.MarkConnecting();
        client.MarkConnectionFailed(failure);
        client.StartSession("session-a");

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
    }

    [Fact]
    public async Task Core_start_session_async_does_not_start_reliable_push_after_connection_failure()
    {
        var client = new LakonaGameClientCore();
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");
        var applied = new List<string>();

        client.MarkConnecting();
        client.MarkConnectionFailed(failure);
        await client.StartSessionAsync("session-a", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.ProcessReliablePushAsync(
                ReliablePushSequence.From(1),
                "payload",
                (payload, _) =>
                {
                    applied.Add(payload);
                    return default;
                },
                (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted()),
                TestContext.Current.CancellationToken);
        });

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Empty(applied);
    }

    [Fact]
    public async Task Core_start_session_async_resets_reliable_push_when_connection_fails_during_cursor_load()
    {
        var cursorStore = new DelayedReliablePushCursorStore();
        var client = new LakonaGameClientCore(cursorStore);
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");
        var applied = new List<string>();

        client.MarkConnecting();
        var startTask = client.StartSessionAsync("session-a", TestContext.Current.CancellationToken);
        await cursorStore.WaitForLoadAsync(TestContext.Current.CancellationToken);
        client.MarkConnectionFailed(failure);

        cursorStore.ReleaseLoad();
        await startTask;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.ProcessReliablePushAsync(
                ReliablePushSequence.From(1),
                "payload",
                (payload, _) =>
                {
                    applied.Add(payload);
                    return default;
                },
                (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted()),
                TestContext.Current.CancellationToken);
        });

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Empty(applied);
    }

    [Fact]
    public void Core_start_session_promotes_ready_client_to_active_session()
    {
        var client = new LakonaGameClientCore();

        client.MarkConnecting();
        client.MarkReady();
        client.StartSession("session-a", lastReliableSequence: 3);

        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        Assert.Equal("session-a", client.Snapshot.SessionId);
        Assert.Equal(3, client.Snapshot.LastReliableSequence);
    }

    private sealed class DelayedReliablePushCursorStore : IReliablePushCursorStore
    {
        private readonly TaskCompletionSource _loadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<long> _loadReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<long> LoadAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            _loadStarted.TrySetResult();
            return await _loadReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask SaveAsync(
            string sessionId,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask ClearAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public async ValueTask WaitForLoadAsync(CancellationToken cancellationToken)
        {
            await _loadStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void ReleaseLoad(long lastReliableSequence = 0)
        {
            _loadReleased.TrySetResult(lastReliableSequence);
        }
    }
}
