using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Core;
using Lakona.Tests;
using System.Threading.Channels;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class LakonaGameClientLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Session_termination_does_not_change_a_live_connection_state()
    {
        var transport = new GameClientTestTransport();
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        transport.EstablishSession();
        await transport.EstablishedAcknowledged.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        transport.Push(GameSessionNotificationRpcIds.ServiceId, GameSessionNotificationRpcIds.TerminatedNotificationId,
            LakonaInternalCodec.EncodeSessionTerminationNotice(new SessionTerminationNotice(SessionTerminationReason.Application, "Session ended.")));
        // The response follows the synchronous termination notification in ordered dispatch.
        Assert.Equal(7, await client.Dispatcher.CallAsync(new RpcMethod<int, int>(42, 1), 7, TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(ClientSessionPhase.Terminated, client.Snapshot.Phase);
        AssertState(client, LakonaGameConnectionState.Connected);
        client.EnsureApiReady();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_canceling_initial_connect_or_handshake_disposes_client(bool handshake)
    {
        var release = GameClientTestTransport.Signal();
        var transport = new GameClientTestTransport
        {
            ConnectRelease = handshake ? null : release,
            HandshakeRelease = handshake ? release : null
        };
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        using var cancellation = new CancellationTokenSource();
        var connecting = client.ConnectAsync(cancellation.Token).AsTask();
        await (handshake ? transport.HandshakeEntered.Task : transport.ConnectEntered.Task).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Connecting);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        AssertState(client, LakonaGameConnectionState.Disposed);
        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
    }

    [Fact]
    public async Task Late_transport_connect_cannot_overwrite_disposed_state()
    {
        var release = GameClientTestTransport.Signal();
        var transport = new GameClientTestTransport { ConnectRelease = release, IgnoreConnectCancellation = true };
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        var connecting = client.ConnectAsync(TestContext.Current.CancellationToken).AsTask();
        await transport.ConnectEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        var disposal = client.DisposeAsync().AsTask();
        try
        {
            await transport.ConnectCanceled.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            AssertState(client, LakonaGameConnectionState.Disposed);
            Assert.False(disposal.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        await disposal.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Disposed);
        Assert.False(transport.HandshakeEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task Stopped_connection_is_visible_while_dispatch_is_paused_before_recovery_starts()
    {
        var first = new GameClientTestTransport();
        var second = new GameClientTestTransport();
        var transports = new Queue<GameClientTestTransport>([first, second]);
        var scheduler = new ControlledRecoveryScheduler();
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(
            () => transports.Dequeue(), new IntegerSerializer()) { RecoveryScheduler = scheduler },
            rpc => rpc.RegisterNotificationHandler<int>(new RpcNotificationMethod<int>(42, 2), _ => default));
        var terminalEvents = 0;
        client.Disconnected += _ => Interlocked.Increment(ref terminalEvents);
        var changes = ObserveChanges(client);
        var context = new PausableContext();
        var previous = SynchronizationContext.Current;
        Task connecting;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            connecting = client.ConnectAsync(TestContext.Current.CancellationToken).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await connecting.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Connected);
        await AssertChange(changes, LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting);
        await AssertChange(changes, LakonaGameConnectionState.Connecting, LakonaGameConnectionState.Connected);
        var stopped = GameClientTestTransport.Signal();
        using var registration = first.ReceiveCancellation.Register(() => stopped.TrySetResult());
        context.Pause = true;
        try
        {
            first.Push(42, 2, BitConverter.GetBytes(1));
            await context.Posted.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            first.Disconnect();
            await stopped.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            // The peer still reports connected: no business RPC is needed to detect runtime termination.
            Assert.True(first.IsConnected);
            AssertState(client, LakonaGameConnectionState.Reconnecting);
            await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Reconnecting);
            client.EnsureApiReady(); // Existing proxy access during ordered draining is unchanged.
            Assert.False(scheduler.Waiting.Task.IsCompleted);
            Assert.Equal(0, terminalEvents);
        }
        finally { context.Release(); }
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        scheduler.Step();
        await first.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Connected);
        Assert.Equal(0, terminalEvents);
        await AssertChange(changes, LakonaGameConnectionState.Reconnecting, LakonaGameConnectionState.Connected);
        await client.DisposeAsync();
        await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Disposed);
        Assert.False(changes.Reader.TryRead(out _));
    }

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
        var changes = ObserveChanges(client);
        if (badHandshake)
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(TestContext.Current.CancellationToken).AsTask());
        else
            Assert.Same(transport.ConnectError, await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync(TestContext.Current.CancellationToken).AsTask()));
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        AssertState(client, LakonaGameConnectionState.Disposed);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(TestContext.Current.CancellationToken).AsTask());
        await AssertChange(changes, LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting);
        await AssertChange(changes, LakonaGameConnectionState.Connecting, LakonaGameConnectionState.Disposed);
        Assert.False(changes.Reader.TryRead(out _));
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
        AssertState(client, LakonaGameConnectionState.Created);
        var connecting = client.ConnectAsync(TestContext.Current.CancellationToken).AsTask();
        await (handshake ? transport.HandshakeEntered.Task : transport.ConnectEntered.Task).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Connecting);
        await client.DisposeAsync().AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(1, transport.DisposeCount);
        AssertState(client, LakonaGameConnectionState.Disposed);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
    }

    [Fact]
    public async Task Concurrent_disposal_callers_join_the_same_cleanup()
    {
        var release = GameClientTestTransport.Signal();
        var transport = new GameClientTestTransport { DisposeRelease = release };
        var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Connected);
        var first = client.DisposeAsync().AsTask();
        await transport.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        var second = client.DisposeAsync().AsTask();
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            AssertState(client, LakonaGameConnectionState.Disposed);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Disposed);
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
        AssertState(client, LakonaGameConnectionState.Connected);
        Assert.Equal(ClientSessionPhase.Ready, client.Snapshot.Phase);
        first.EstablishSession();
        await first.EstablishedAcknowledged.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Connected);
        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Reconnecting);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        scheduler.Step();
        await second.HeartbeatEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Reconnecting);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        release.TrySetResult();
        await first.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        AssertState(client, LakonaGameConnectionState.Connected);
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
        AssertState(client, LakonaGameConnectionState.Connected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Heartbeat_failure_recovers_once_and_fails_old_calls_without_replaying_them(
        bool disconnectOnTimeout, bool invalidReply)
    {
        var ct = TestContext.Current.CancellationToken;
        var first = new GameClientTestTransport
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200),
            SuppressBusinessReplies = true,
            DisconnectOnHeartbeatCancellation = disconnectOnTimeout
        };
        var replayRelease = GameClientTestTransport.Signal();
        var second = new GameClientTestTransport { HeartbeatRelease = replayRelease };
        var scheduler = new ControlledRecoveryScheduler();
        var transports = new Queue<GameClientTestTransport>([first, second]);
        var connections = 0;
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            Interlocked.Increment(ref connections);
            return transports.Dequeue();
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var changes = ObserveChanges(client);
        await client.ConnectAsync(ct);
        first.EstablishSession();
        await first.EstablishedAcknowledged.Task.WaitAsync(Deadline, ct);
        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        await AssertChange(changes, LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting);
        await AssertChange(changes, LakonaGameConnectionState.Connecting, LakonaGameConnectionState.Connected);

        var dispatcher = client.Dispatcher;
        var method = new RpcMethod<int, int>(42, 1);
        var oldCall = dispatcher.CallAsync(method, 17, ct).AsTask();
        await first.BusinessEntered.Task.WaitAsync(Deadline, ct);
        if (invalidReply)
        {
            first.HeartbeatReplyStatus = (GameHeartbeatStatus)int.MaxValue;
        }
        else
        {
            first.SuppressHeartbeatReplies = true;
            await first.HeartbeatSuppressed.Task.WaitAsync(Deadline, ct);
        }
        await scheduler.Waiting.Task.WaitAsync(Deadline, ct);
        await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Reconnecting);
        AssertState(client, LakonaGameConnectionState.Reconnecting);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.CallAsync(method, 99, ct).AsTask());
        await Assert.ThrowsAnyAsync<Exception>(() => oldCall.WaitAsync(Deadline, ct));
        Assert.True(oldCall.IsCompleted);
        Assert.Equal(1, first.BusinessRequestCount);
        Assert.Equal(1, scheduler.DelayCount);
        Assert.Equal(1, connections);

        scheduler.Step();
        await second.HeartbeatEntered.Task.WaitAsync(Deadline, ct);
        AssertState(client, LakonaGameConnectionState.Reconnecting);
        Assert.Equal(0, second.BusinessRequestCount);
        replayRelease.TrySetResult();
        await AssertChange(changes, LakonaGameConnectionState.Reconnecting, LakonaGameConnectionState.Connected);
        client.EnsureApiReady();
        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        Assert.Same(dispatcher, client.Dispatcher);
        Assert.Equal("ticket", second.Hello!.ResumeTicket);
        Assert.Equal(23, await dispatcher.CallAsync(method, 23, ct).AsTask().WaitAsync(Deadline, ct));
        Assert.Equal(1, second.BusinessRequestCount);
        Assert.Equal(2, connections);
        await first.Disposed.Task.WaitAsync(Deadline, ct);
        Assert.Equal(1, scheduler.DelayCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.False(changes.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Dispose_during_unanswered_heartbeat_does_not_start_recovery()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new GameClientTestTransport
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromHours(1)
        };
        var scheduler = new ControlledRecoveryScheduler();
        var connections = 0;
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            Interlocked.Increment(ref connections);
            return transport;
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminalEvents = 0;
        client.Disconnected += _ => Interlocked.Increment(ref terminalEvents);
        await client.ConnectAsync(ct);
        transport.EstablishSession();
        await transport.EstablishedAcknowledged.Task.WaitAsync(Deadline, ct);
        transport.SuppressHeartbeatReplies = true;
        await transport.HeartbeatSuppressed.Task.WaitAsync(Deadline, ct);

        await client.DisposeAsync().AsTask().WaitAsync(Deadline, ct);

        AssertState(client, LakonaGameConnectionState.Disposed);
        Assert.Equal(0, scheduler.DelayCount);
        Assert.Equal(1, connections);
        Assert.Equal(0, terminalEvents);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_failure_during_previous_cleanup_starts_another_recovery(bool heartbeatTimeout)
    {
        var ct = TestContext.Current.CancellationToken;
        var cleanupRelease = GameClientTestTransport.Signal();
        var first = new GameClientTestTransport { DisposeRelease = cleanupRelease };
        var second = new GameClientTestTransport
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200)
        };
        var third = new GameClientTestTransport();
        var scheduler = new ControlledRecoveryScheduler();
        var transports = new Queue<GameClientTestTransport>([first, second, third]);
        var connections = 0;
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            Interlocked.Increment(ref connections);
            return transports.Dequeue();
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var changes = ObserveChanges(client);
        try
        {
            await client.ConnectAsync(ct);
            first.EstablishSession();
            await first.EstablishedAcknowledged.Task.WaitAsync(Deadline, ct);
            await AssertChange(changes, LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting);
            await AssertChange(changes, LakonaGameConnectionState.Connecting, LakonaGameConnectionState.Connected);
            var dispatcher = client.Dispatcher;
            first.Disconnect();
            Assert.Equal(1, await scheduler.DelayEntries.Reader.ReadAsync(ct).AsTask().WaitAsync(Deadline, ct));
            await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Reconnecting);
            scheduler.Step();
            await first.Disposed.Task.WaitAsync(Deadline, ct);
            await AssertChange(changes, LakonaGameConnectionState.Reconnecting, LakonaGameConnectionState.Connected);
            Assert.Equal(2, connections);
            Assert.False(cleanupRelease.Task.IsCompleted);

            if (heartbeatTimeout)
            {
                second.SuppressHeartbeatReplies = true;
                await second.HeartbeatSuppressed.Task.WaitAsync(Deadline, ct);
            }
            else
            {
                second.Disconnect();
            }
            await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Reconnecting);
            Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
            Assert.Equal(1, scheduler.DelayCount);
            cleanupRelease.TrySetResult();
            Assert.Equal(2, await scheduler.DelayEntries.Reader.ReadAsync(ct).AsTask().WaitAsync(Deadline, ct));
            scheduler.Step();
            await AssertChange(changes, LakonaGameConnectionState.Reconnecting, LakonaGameConnectionState.Connected);
            Assert.Same(dispatcher, client.Dispatcher);
            Assert.Equal(31, await dispatcher.CallAsync(new RpcMethod<int, int>(42, 1), 31, ct).AsTask().WaitAsync(Deadline, ct));
            Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
            Assert.Equal("ticket", third.Hello!.ResumeTicket);
            await second.Disposed.Task.WaitAsync(Deadline, ct);
            Assert.Equal(3, connections);
            Assert.Equal(2, scheduler.DelayCount);
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.DisposeCount);
        }
        finally
        {
            cleanupRelease.TrySetResult();
        }
    }

    [Theory]
    [InlineData(GameHeartbeatStatus.StateLost, ClientSessionPhase.StateLost)]
    [InlineData(GameHeartbeatStatus.Terminated, ClientSessionPhase.Terminated)]
    public async Task Terminal_heartbeat_reply_updates_session_without_reconnecting(
        GameHeartbeatStatus status, ClientSessionPhase phase)
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new GameClientTestTransport
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromSeconds(2)
        };
        var scheduler = new ControlledRecoveryScheduler();
        await using var client = new LakonaGameClientLifecycle(
            new LakonaGameClientOptions(transport, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        await client.ConnectAsync(ct);
        transport.EstablishSession();
        await transport.EstablishedAcknowledged.Task.WaitAsync(Deadline, ct);
        transport.HeartbeatReplyStatus = status;
        GameHeartbeatStatus observed;
        do
        {
            observed = await transport.HeartbeatReplies.Reader.ReadAsync(ct).AsTask().WaitAsync(Deadline, ct);
        } while (observed != status);
        // The following heartbeat is sent only after the preceding reply has been applied.
        Assert.Equal(status, await transport.HeartbeatReplies.Reader.ReadAsync(ct).AsTask().WaitAsync(Deadline, ct));

        Assert.Equal(phase, client.Snapshot.Phase);
        AssertState(client, LakonaGameConnectionState.Connected);
        Assert.Equal(0, scheduler.DelayCount);
        Assert.Equal(0, transport.DisposeCount);
    }

    [Fact]
    public async Task Heartbeat_failure_is_terminal_if_disposing_the_failed_transport_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var failure = new IOException("Failed transport cleanup.");
        var transport = new GameClientTestTransport
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200),
            DisposeError = failure
        };
        var scheduler = new ControlledRecoveryScheduler();
        var connections = 0;
        var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() =>
        {
            Interlocked.Increment(ref connections);
            return transport;
        }, new IntegerSerializer()) { RecoveryScheduler = scheduler });
        var terminal = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += error => terminal.TrySetResult(error);
        try
        {
            await client.ConnectAsync(ct);
            transport.EstablishSession();
            await transport.EstablishedAcknowledged.Task.WaitAsync(Deadline, ct);
            transport.SuppressHeartbeatReplies = true;
            await transport.HeartbeatSuppressed.Task.WaitAsync(Deadline, ct);
            Assert.Same(failure, await terminal.Task.WaitAsync(Deadline, ct));
            AssertState(client, LakonaGameConnectionState.Disconnected);
            Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
            Assert.Equal(ClientConnectionFailureKind.HeartbeatFailed, client.Snapshot.Failure?.Kind);
            Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
            Assert.Equal(0, scheduler.DelayCount);
            Assert.Equal(1, connections);
        }
        finally
        {
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(
                () => client.DisposeAsync().AsTask().WaitAsync(Deadline, ct)));
        }
        AssertState(client, LakonaGameConnectionState.Disposed);
        Assert.Equal(1, transport.DisposeCount);
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
        AssertState(client, LakonaGameConnectionState.Reconnecting);
        scheduler.Step();
        await first.Disposed.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        client.EnsureApiReady();
        Assert.Equal(1, failed.DisposeCount);
        Assert.True(replacement.HeartbeatEntered.Task.IsCompleted);
        AssertState(client, LakonaGameConnectionState.Connected);
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
        AssertState(client, LakonaGameConnectionState.Disconnected);
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
        var changes = ObserveChanges(client);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        first.Disconnect();
        await scheduler.Waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        scheduler.Step();
        await terminal.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(1, second.DisposeCount);
        Assert.False(second.HeartbeatEntered.Task.IsCompleted);
        AssertState(client, LakonaGameConnectionState.Disconnected);
        Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
        await client.DisposeAsync();
        await AssertChange(changes, LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting);
        await AssertChange(changes, LakonaGameConnectionState.Connecting, LakonaGameConnectionState.Connected);
        await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Reconnecting);
        await AssertChange(changes, LakonaGameConnectionState.Reconnecting, LakonaGameConnectionState.Disconnected);
        await AssertChange(changes, LakonaGameConnectionState.Disconnected, LakonaGameConnectionState.Disposed);
        Assert.False(changes.Reader.TryRead(out _));
    }

    [Fact]
    public async Task State_observers_are_isolated_and_can_dispose_without_holding_lifecycle_locks()
    {
        var transport = new GameClientTestTransport();
        var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        client.ConnectionStateChanged += _ => throw new InvalidOperationException("observer failure");
        client.ConnectionStateChanged += change =>
        {
            // Access from another thread detects invocation under a lifecycle lock.
            Task.Run(() => client.ConnectionState).WaitAsync(Deadline).GetAwaiter().GetResult();
            if (change.CurrentState == LakonaGameConnectionState.Connected)
                client.DisposeAsync().AsTask().WaitAsync(Deadline).GetAwaiter().GetResult();
        };
        var changes = ObserveChanges(client);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await AssertChange(changes, LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting);
        await AssertChange(changes, LakonaGameConnectionState.Connecting, LakonaGameConnectionState.Connected);
        await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Disposed);
        AssertState(client, LakonaGameConnectionState.Disposed);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task Slow_state_observer_does_not_block_connect_or_disposal_and_unsubscription_stops_future_notifications()
    {
        var transport = new GameClientTestTransport();
        var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(transport, new IntegerSerializer()));
        using var release = new ManualResetEventSlim();
        var entered = GameClientTestTransport.Signal();
        var observerCalls = 0;
        Action<LakonaGameConnectionStateChange> observer = _ =>
        {
            Interlocked.Increment(ref observerCalls);
            entered.TrySetResult();
            if (!release.Wait(Deadline)) throw new TimeoutException();
        };
        client.ConnectionStateChanged += observer;
        var changes = ObserveChanges(client);
        try
        {
            await client.ConnectAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            client.ConnectionStateChanged -= observer;
            await client.DisposeAsync().AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
            AssertState(client, LakonaGameConnectionState.Disposed);
        }
        finally { release.Set(); }
        await AssertChange(changes, LakonaGameConnectionState.Created, LakonaGameConnectionState.Connecting);
        await AssertChange(changes, LakonaGameConnectionState.Connecting, LakonaGameConnectionState.Connected);
        await AssertChange(changes, LakonaGameConnectionState.Connected, LakonaGameConnectionState.Disposed);
        Assert.Equal(2, Volatile.Read(ref observerCalls)); // Already queued transitions retain their subscriber snapshot.
        var lateChanges = ObserveChanges(client);
        await client.DisposeAsync();
        Assert.False(lateChanges.Reader.TryRead(out _)); // No replay or duplicate Disposed transition.
    }

    private static Channel<LakonaGameConnectionStateChange> ObserveChanges(LakonaGameClientLifecycle client)
    {
        var changes = Channel.CreateUnbounded<LakonaGameConnectionStateChange>();
        client.ConnectionStateChanged += change => changes.Writer.TryWrite(change);
        return changes;
    }

    private static async Task AssertChange(Channel<LakonaGameConnectionStateChange> changes,
        LakonaGameConnectionState previous, LakonaGameConnectionState current)
    {
        var change = await changes.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(previous, change.PreviousState);
        Assert.Equal(current, change.CurrentState);
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
        AssertState(client, LakonaGameConnectionState.Disposed);
    }

    private static void AssertState(LakonaGameClientLifecycle client, LakonaGameConnectionState expected)
    {
        Assert.Equal(expected, client.ConnectionState);
    }

    private sealed class PausableContext : SynchronizationContext
    {
        public volatile bool Pause;
        private SendOrPostCallback? _callback;
        private object? _state;
        public TaskCompletionSource Posted { get; } = GameClientTestTransport.Signal();
        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (!Pause) { ThreadPool.QueueUserWorkItem(_ => callback(state)); return; }
            _state = state;
            _callback = callback;
            Posted.TrySetResult();
        }
        public void Release()
        {
            Pause = false;
            Interlocked.Exchange(ref _callback, null)?.Invoke(_state);
        }
    }
}
