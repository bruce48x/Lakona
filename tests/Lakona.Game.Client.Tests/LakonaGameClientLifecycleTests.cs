using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Core;
using Lakona.Tests;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class LakonaGameClientLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_connection_failure_is_terminal_and_releases_transport(bool badHandshake)
    {
        var transport = new GameClientTestTransport
        {
            ConnectError = badHandshake ? null : new IOException("connect failed"),
            ProtocolVersion = badHandshake ? 99 : 1
        };
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        if (badHandshake)
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(TestContext.Current.CancellationToken).AsTask());
        else
            Assert.Same(transport.ConnectError, await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync(TestContext.Current.CancellationToken).AsTask()));
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_cancels_and_joins_initial_connect_or_handshake(bool handshake)
    {
        var release = GameClientTestTransport.Signal();
        var transport = new GameClientTestTransport
        {
            ConnectRelease = handshake ? null : release,
            HandshakeRelease = handshake ? release : null
        };
        var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        var connecting = client.ConnectAsync(TestContext.Current.CancellationToken).AsTask();
        await (handshake ? transport.HandshakeEntered.Task : transport.ConnectEntered.Task).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await client.DisposeAsync().AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
    }

    [Fact]
    public async Task Concurrent_disposal_callers_join_the_same_cleanup()
    {
        var release = GameClientTestTransport.Signal();
        var transport = new GameClientTestTransport { DisposeRelease = release };
        var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        var first = client.DisposeAsync().AsTask();
        await transport.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        var second = client.DisposeAsync().AsTask();
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task Recovery_keeps_dispatcher_and_callbacks_and_waits_for_replay_heartbeat()
    {
        var first = new GameClientTestTransport();
        var release = GameClientTestTransport.Signal();
        var second = new GameClientTestTransport { HeartbeatRelease = release };
        var scheduler = new ControlledRecoveryScheduler();
        var transports = new Queue<GameClientTestTransport>([first, second]);
        var options = new LakonaGameClientOptions(() => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler };
        var bindings = 0;
        var notification = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new LakonaGameClientLifecycle(options, rpc =>
        {
            bindings++;
            rpc.RegisterNotificationHandler<int>(new RpcNotificationMethod<int>(42, 2), value =>
            {
                notification.TrySetResult(value);
                return default;
            });
        });
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        var dispatcher = client.Dispatcher;
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(TestContext.Current.CancellationToken).AsTask());
        client.EnsureApiReady();
        first.EstablishSession();
        await first.EstablishedAcknowledged.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        scheduler.Step();
        await second.HeartbeatEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        release.TrySetResult();
        await first.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        client.EnsureApiReady();
        Assert.Same(dispatcher, client.Dispatcher);
        Assert.Equal("ticket", second.Hello!.ResumeTicket);
        Assert.Equal(2, bindings);
        Assert.Equal(17, await dispatcher.CallAsync(new RpcMethod<int, int>(42, 1), 17, TestContext.Current.CancellationToken));
        second.Push(42, 2, BitConverter.GetBytes(23));
        Assert.Equal(23, await notification.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        // Previous transport cleanup must not clear the replacement's dispatch target.
        Assert.Equal(1, first.DisposeCount);
        client.EnsureApiReady();
    }

    [Fact]
    public async Task Failed_recovery_candidate_is_released_before_retry()
    {
        var first = new GameClientTestTransport();
        var failed = new GameClientTestTransport { ConnectError = new IOException("retry") };
        var replacement = new GameClientTestTransport();
        var transports = new Queue<GameClientTestTransport>([first, failed, replacement]);
        var scheduler = new ControlledRecoveryScheduler();
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler });
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        scheduler.Step();
        await failed.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        scheduler.Step();
        await first.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        client.EnsureApiReady();
        Assert.Equal(1, failed.DisposeCount);
        Assert.True(replacement.HeartbeatEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task Expired_recovery_window_reports_terminal_state_without_another_attempt()
    {
        var first = new GameClientTestTransport();
        var attempts = 0;
        var scheduler = new ControlledRecoveryScheduler();
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            if (++attempts == 1) return first;
            scheduler.Now += TimeSpan.FromMinutes(2);
            throw new IOException("unavailable");
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        client.Disconnected += _ => terminal.TrySetResult();
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        scheduler.Step();
        await terminal.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(2, attempts);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
    }

    [Theory]
    [InlineData(GameSessionRecoveryStatus.StateLost)]
    [InlineData(GameSessionRecoveryStatus.StateRefreshRequired)]
    [InlineData(GameSessionRecoveryStatus.Terminated)]
    public async Task Recovery_rejection_is_terminal_and_disposes_candidate(GameSessionRecoveryStatus status)
    {
        var first = new GameClientTestTransport();
        var second = new GameClientTestTransport { RecoveryStatus = status };
        var transports = new Queue<GameClientTestTransport>([first, second]);
        var scheduler = new ControlledRecoveryScheduler();
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = GameClientTestTransport.Signal();
        client.Disconnected += _ => terminal.TrySetResult();
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        scheduler.Step();
        await terminal.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(1, second.DisposeCount);
        Assert.False(second.HeartbeatEntered.Task.IsCompleted);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_cancels_recovery_delay_or_handshake_without_terminal_event(bool handshake)
    {
        var first = new GameClientTestTransport();
        var second = new GameClientTestTransport { HandshakeRelease = GameClientTestTransport.Signal() };
        var transports = new Queue<GameClientTestTransport>([first, second]);
        var scheduler = new ControlledRecoveryScheduler();
        var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminalEvents = 0;
        client.Disconnected += _ => terminalEvents++;
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        if (handshake)
        {
            scheduler.Step();
            await second.HandshakeEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
        await client.DisposeAsync().AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(0, terminalEvents);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(handshake ? 1 : 0, second.DisposeCount);
    }
}
