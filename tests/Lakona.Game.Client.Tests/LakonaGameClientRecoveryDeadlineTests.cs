using System.Threading.Channels;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Tests;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class LakonaGameClientRecoveryDeadlineTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("connect")]
    [InlineData("handshake")]
    [InlineData("replay")]
    public async Task Recovery_deadline_expires_while_connection_step_is_waiting(string step)
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler();
        var first = new GameClientTestTransport { ResumeWindow = RecoveryWindow };
        var release = GameClientTestTransport.Signal();
        var second = new GameClientTestTransport
        {
            ConnectRelease = step == "connect" ? release : null,
            HandshakeRelease = step == "handshake" ? release : null,
            HeartbeatRelease = step == "replay" ? release : null
        };
        var transports = new Queue<GameClientTestTransport>([first, second]);
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        var terminalCount = 0;
        client.Disconnected += _ =>
        {
            Interlocked.Increment(ref terminalCount);
            terminal.TrySetResult();
        };
        await ConnectSessionAsync(client, first, ct);
        first.Disconnect();
        await WaitForRecoveryDelaysAsync(scheduler, ct);
        scheduler.Advance(scheduler.RetryDelay);
        var entered = step switch
        {
            "connect" => second.ConnectEntered,
            "handshake" => second.HandshakeEntered,
            _ => second.HeartbeatEntered
        };
        await entered.Task.WaitAsync(WaitLimit, ct);

        scheduler.Advance(RecoveryWindow - scheduler.RetryDelay);

        await terminal.Task.WaitAsync(WaitLimit, ct);
        Assert.Equal(LakonaGameConnectionState.Disconnected, client.ConnectionState);
        Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        await second.Disposed.Task.WaitAsync(WaitLimit, ct);
        Assert.Equal(1, terminalCount);
    }

    [Fact]
    public async Task Deadline_expires_during_backoff_without_starting_another_connection()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler { RetryDelay = RecoveryWindow + TimeSpan.FromSeconds(1) };
        var first = new GameClientTestTransport { ResumeWindow = RecoveryWindow };
        var connections = 0;
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            Interlocked.Increment(ref connections);
            return first;
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        client.Disconnected += _ => terminal.TrySetResult();
        await ConnectSessionAsync(client, first, ct);
        first.Disconnect();
        // Both the clipped backoff and deadline are due at the original window end.
        await WaitForDelayAsync(scheduler.Delays, RecoveryWindow, ct);
        await WaitForDelayAsync(scheduler.Delays, RecoveryWindow, ct);

        scheduler.Advance(RecoveryWindow);

        await terminal.Task.WaitAsync(WaitLimit, ct);
        await client.DisposeAsync().AsTask().WaitAsync(WaitLimit, ct);
        Assert.Equal(1, connections);
        Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
    }

    [Fact]
    public async Task Failed_attempt_does_not_restart_the_recovery_deadline()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler();
        var first = new GameClientTestTransport { ResumeWindow = RecoveryWindow };
        var failed = new GameClientTestTransport { ConnectError = new IOException("Connection rejected.") };
        var pending = new GameClientTestTransport { HandshakeRelease = GameClientTestTransport.Signal() };
        var transports = new Queue<GameClientTestTransport>([first, failed, pending]);
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        client.Disconnected += _ => terminal.TrySetResult();
        await ConnectSessionAsync(client, first, ct);
        first.Disconnect();
        await WaitForRecoveryDelaysAsync(scheduler, ct);
        scheduler.Advance(scheduler.RetryDelay);
        await failed.Disposed.Task.WaitAsync(WaitLimit, ct);
        await WaitForDelayAsync(scheduler.Delays, scheduler.RetryDelay, ct);
        scheduler.Advance(RecoveryWindow - scheduler.RetryDelay - TimeSpan.FromSeconds(1));
        await pending.HandshakeEntered.Task.WaitAsync(WaitLimit, ct);
        Assert.False(terminal.Task.IsCompleted);

        scheduler.Advance(TimeSpan.FromSeconds(1));

        await terminal.Task.WaitAsync(WaitLimit, ct);
        await pending.Disposed.Task.WaitAsync(WaitLimit, ct);
        Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
        Assert.Empty(transports);
    }

    [Fact]
    public async Task Deadline_expires_during_failed_heartbeat_cleanup_and_dispose_joins_cleanup()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler();
        var cleanupRelease = GameClientTestTransport.Signal();
        var first = new GameClientTestTransport
        {
            ResumeWindow = RecoveryWindow,
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            DisposeRelease = cleanupRelease
        };
        var connections = 0;
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            Interlocked.Increment(ref connections);
            return first;
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        var terminalCount = 0;
        client.Disconnected += _ =>
        {
            Interlocked.Increment(ref terminalCount);
            terminal.TrySetResult();
        };
        try
        {
            await ConnectSessionAsync(client, first, ct);
            first.HeartbeatReplyStatus = (GameHeartbeatStatus)999;
            await first.Disposed.Task.WaitAsync(WaitLimit, ct);
            await WaitForDelayAsync(scheduler.Delays, RecoveryWindow, ct);

            scheduler.Advance(RecoveryWindow);

            await terminal.Task.WaitAsync(WaitLimit, ct);
            Assert.Equal(LakonaGameConnectionState.Disconnected, client.ConnectionState);
            Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
            var disposal = client.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            cleanupRelease.TrySetResult();
            await disposal.WaitAsync(WaitLimit, ct);
            Assert.Equal(1, connections);
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, terminalCount);
        }
        finally { cleanupRelease.TrySetResult(); }
    }

    [Fact]
    public async Task Expired_uncooperative_connect_cannot_publish_and_remains_owned_until_disposal()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler();
        var connectRelease = GameClientTestTransport.Signal();
        var cleanupRelease = GameClientTestTransport.Signal();
        var first = new GameClientTestTransport { ResumeWindow = RecoveryWindow };
        var second = new GameClientTestTransport
        {
            ConnectRelease = connectRelease,
            IgnoreConnectCancellation = true,
            DisposeRelease = cleanupRelease
        };
        var transports = new Queue<GameClientTestTransport>([first, second]);
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        var terminalCount = 0;
        client.Disconnected += _ =>
        {
            Interlocked.Increment(ref terminalCount);
            terminal.TrySetResult();
        };
        try
        {
            await ConnectSessionAsync(client, first, ct);
            first.Disconnect();
            await WaitForRecoveryDelaysAsync(scheduler, ct);
            scheduler.Advance(scheduler.RetryDelay);
            await second.ConnectEntered.Task.WaitAsync(WaitLimit, ct);
            scheduler.Advance(RecoveryWindow - scheduler.RetryDelay);
            await terminal.Task.WaitAsync(WaitLimit, ct);
            await second.ConnectCanceled.Task.WaitAsync(WaitLimit, ct);
            Assert.Equal(LakonaGameConnectionState.Disconnected, client.ConnectionState);
            Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
            connectRelease.TrySetResult();

            await second.Disposed.Task.WaitAsync(WaitLimit, ct);
            Assert.Equal(LakonaGameConnectionState.Disconnected, client.ConnectionState);
            Assert.False(second.HandshakeEntered.Task.IsCompleted);
            var disposal = client.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            cleanupRelease.TrySetResult();
            await disposal.WaitAsync(WaitLimit, ct);
            Assert.Equal(LakonaGameConnectionState.Disposed, client.ConnectionState);
            Assert.Equal(1, terminalCount);
            Assert.Equal(1, second.DisposeCount);
        }
        finally
        {
            connectRelease.TrySetResult();
            cleanupRelease.TrySetResult();
        }
    }

    [Fact]
    public async Task Successful_publication_cancels_deadline_but_next_fault_is_bounded_during_predecessor_cleanup()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler();
        var cleanupRelease = GameClientTestTransport.Signal();
        var first = new GameClientTestTransport { ResumeWindow = RecoveryWindow, DisposeRelease = cleanupRelease };
        var second = new GameClientTestTransport { ResumeWindow = RecoveryWindow };
        var transports = new Queue<GameClientTestTransport>([first, second]);
        var connections = 0;
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            Interlocked.Increment(ref connections);
            return transports.Dequeue();
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        var terminalCount = 0;
        client.Disconnected += _ =>
        {
            Interlocked.Increment(ref terminalCount);
            terminal.TrySetResult();
        };
        try
        {
            await ConnectSessionAsync(client, first, ct);
            first.Disconnect();
            await WaitForRecoveryDelaysAsync(scheduler, ct);
            scheduler.Advance(scheduler.RetryDelay);
            await first.Disposed.Task.WaitAsync(WaitLimit, ct);
            await WaitForDelayAsync(scheduler.CanceledDelays, RecoveryWindow, ct);
            scheduler.Advance(RecoveryWindow);
            Assert.Equal(LakonaGameConnectionState.Connected, client.ConnectionState);
            Assert.Equal(23, await client.Dispatcher.CallAsync(new RpcMethod<int, int>(42, 1), 23, ct)
                .AsTask().WaitAsync(WaitLimit, ct));
            Assert.False(terminal.Task.IsCompleted);

            second.Disconnect();
            await WaitForDelayAsync(scheduler.Delays, RecoveryWindow, ct);
            scheduler.Advance(RecoveryWindow);

            await terminal.Task.WaitAsync(WaitLimit, ct);
            Assert.Equal(LakonaGameConnectionState.Disconnected, client.ConnectionState);
            Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
            var disposal = client.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            cleanupRelease.TrySetResult();
            await disposal.WaitAsync(WaitLimit, ct);
            Assert.Equal(2, connections);
            Assert.Equal(1, terminalCount);
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.DisposeCount);
        }
        finally { cleanupRelease.TrySetResult(); }
    }

    [Fact]
    public async Task Active_dispose_cancels_recovery_without_a_terminal_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler();
        var first = new GameClientTestTransport { ResumeWindow = RecoveryWindow };
        var second = new GameClientTestTransport { HandshakeRelease = GameClientTestTransport.Signal() };
        var transports = new Queue<GameClientTestTransport>([first, second]);
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminalCount = 0;
        client.Disconnected += _ => Interlocked.Increment(ref terminalCount);
        await ConnectSessionAsync(client, first, ct);
        first.Disconnect();
        await WaitForRecoveryDelaysAsync(scheduler, ct);
        scheduler.Advance(scheduler.RetryDelay);
        await second.HandshakeEntered.Task.WaitAsync(WaitLimit, ct);

        await client.DisposeAsync().AsTask().WaitAsync(WaitLimit, ct);
        scheduler.Advance(RecoveryWindow);

        Assert.Equal(LakonaGameConnectionState.Disposed, client.ConnectionState);
        Assert.Equal(0, terminalCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task Late_candidate_session_cursor_load_cannot_revive_an_expired_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = new VirtualRecoveryScheduler();
        var cursor = new DelayedRecoveryCursorStore();
        var first = new GameClientTestTransport { ResumeWindow = RecoveryWindow };
        var second = new GameClientTestTransport { HeartbeatRelease = GameClientTestTransport.Signal() };
        var transports = new Queue<GameClientTestTransport>([first, second]);
        var staleCallback = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer())
        {
            RecoveryScheduler = scheduler,
            ReliablePushCursorStore = cursor
        }, rpc => Assert.IsType<RpcClientRuntime>(rpc).NotificationHandlerException +=
            failure => staleCallback.TrySetResult(failure.Exception));
        var terminal = GameClientTestTransport.Signal();
        client.Disconnected += _ => terminal.TrySetResult();
        try
        {
            await ConnectSessionAsync(client, first, ct);
            first.Disconnect();
            await WaitForRecoveryDelaysAsync(scheduler, ct);
            scheduler.Advance(scheduler.RetryDelay);
            await second.HeartbeatEntered.Task.WaitAsync(WaitLimit, ct);
            second.EstablishSession();
            await cursor.LoadEntered.Task.WaitAsync(WaitLimit, ct);
            scheduler.Advance(RecoveryWindow - scheduler.RetryDelay);
            await terminal.Task.WaitAsync(WaitLimit, ct);
            Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);

            cursor.LoadRelease.TrySetResult();

            Assert.IsAssignableFrom<OperationCanceledException>(await staleCallback.Task.WaitAsync(WaitLimit, ct));
            Assert.Equal(LakonaGameConnectionState.Disconnected, client.ConnectionState);
            Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
            Assert.False(second.EstablishedAcknowledged.Task.IsCompleted);
            await client.DisposeAsync().AsTask().WaitAsync(WaitLimit, ct);
            Assert.Equal(1, second.DisposeCount);
        }
        finally { cursor.LoadRelease.TrySetResult(); }
    }

    private static async Task ConnectSessionAsync(
        LakonaGameClientLifecycle client, GameClientTestTransport transport, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(cancellationToken);
        transport.EstablishSession();
        await transport.EstablishedAcknowledged.Task.WaitAsync(WaitLimit, cancellationToken);
    }

    private static async Task WaitForRecoveryDelaysAsync(
        VirtualRecoveryScheduler scheduler, CancellationToken cancellationToken)
    {
        var first = await scheduler.Delays.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(WaitLimit, cancellationToken);
        var second = await scheduler.Delays.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(WaitLimit, cancellationToken);
        Assert.Contains(RecoveryWindow, new[] { first, second });
        Assert.Contains(scheduler.RetryDelay, new[] { first, second });
    }

    private static async Task WaitForDelayAsync(
        Channel<TimeSpan> delays, TimeSpan expected, CancellationToken cancellationToken)
    {
        TimeSpan actual;
        do
        {
            actual = await delays.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(WaitLimit, cancellationToken);
        } while (actual != expected);
    }

    private sealed class DelayedRecoveryCursorStore : IReliablePushCursorStore
    {
        private int _loads;
        public TaskCompletionSource LoadEntered { get; } = GameClientTestTransport.Signal();
        public TaskCompletionSource LoadRelease { get; } = GameClientTestTransport.Signal();

        public async ValueTask<long> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _loads) == 1) return 0;
            LoadEntered.TrySetResult();
            // Storage may finish after cancellation; the owner must reject the stale result.
            await LoadRelease.Task;
            return 7;
        }

        public ValueTask SaveAsync(string sessionId, long sequence, CancellationToken cancellationToken = default) => default;
        public ValueTask ClearAsync(string sessionId, CancellationToken cancellationToken = default) => default;
    }
}
