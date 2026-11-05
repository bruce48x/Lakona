using System.Collections.Concurrent;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ReliablePushRecoveryOrderingTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recovery_heartbeat_and_concurrent_publish_deliver_both_records_in_order(bool pauseReplay)
    {
        var sessions = new InMemoryGameSessionRegistry();
        var key = await ResumeAsync(sessions);
        var callback = new RecordingCallback();
        await using var connection = new TestCallbackConnection(sessions, "resumed", callback);
        var outbox = new PausedOutbox();
        var runtime = Runtime(sessions, connection, outbox);
        var heartbeat = new GameHeartbeatService(sessions, runtime);
        Assert.Equal(ClientNotificationStatus.Retained, await runtime.PublishAsync(key, Command(key), Token));

        Task<GameHeartbeatReply> Replay() => heartbeat.HeartbeatAsync(
            "resumed", new GameHeartbeatRequest { SessionId = key.SessionId }, Token).AsTask();
        Task<ClientNotificationStatus> Publish() => runtime.PublishAsync(key, Command(key), Token).AsTask();
        outbox.PauseReplay = pauseReplay;
        Task first = pauseReplay ? Replay() : Publish();
        try
        {
            await outbox.Entered.Task.WaitAsync(Guard, Token);
            Task second = pauseReplay ? Publish() : Replay();
            outbox.Continue.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(Guard, Token);
        }
        finally { outbox.Continue.TrySetResult(); }

        // Only one heartbeat ran. Nothing may overtake record 1 or wait for a later heartbeat.
        Assert.Equal(new long[] { 1, 2 }, callback.Sequences.ToArray());
        Assert.False(await sessions.IsReliableReplayPendingAsync(key, Token));
    }

    private static async Task<GameSessionKey> ResumeAsync(InMemoryGameSessionRegistry sessions, bool disconnectFirst = true)
    {
        var key = await sessions.StartNewSessionAsync("owner", Token);
        await sessions.SetReliablePushPolicyAsync(key, true, Token);
        await sessions.PrepareProfiledSessionBindingAsync(key, "initial", "json", Token);
        await sessions.CommitSessionBindingAsync(key, "initial", Token);
        if (disconnectFirst)
            await sessions.MarkConnectionDisconnectedAsync("initial", Token);
        await sessions.PrepareProfiledSessionBindingAsync(key, "resumed", "json", Token);
        await sessions.CommitSessionBindingAsync(key, "resumed", Token);
        Assert.True(await sessions.IsReliableReplayPendingAsync(key, Token));
        return key;
    }

    [Fact]
    public async Task Business_gate_opens_only_after_the_last_retained_write_succeeds()
    {
        var sessions = new InMemoryGameSessionRegistry();
        var key = await ResumeAsync(sessions);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new RecordingCallback
        {
            BeforeSend = async (sequence, cancellationToken) =>
            {
                if (sequence != 2) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        await using var connection = new TestCallbackConnection(sessions, "resumed", callback);
        var runtime = Runtime(sessions, connection, new InMemoryReliablePushOutbox(new ReliablePushOptions()));
        Assert.Equal(ClientNotificationStatus.Retained, await runtime.PublishAsync(key, Command(key), Token));
        Assert.Equal(ClientNotificationStatus.Retained, await runtime.PublishAsync(key, Command(key), Token));
        var states = new GameHandshakeConnectionStateRegistry();
        using var slots = new SemaphoreSlim(0, 1);
        await using var lease = states.RegisterPending("resumed", Guard, slots, NullLogger.Instance);
        states.MarkComplete("resumed");
        var gate = new GameHandshakeRpcGate(states, sessions);
        var business = new RpcSessionRequestGateContext(new RpcConnectionInfo("resumed"), 1, 1);
        var heartbeat = new GameHeartbeatService(sessions, runtime);
        var replay = heartbeat.HeartbeatAsync("resumed", new GameHeartbeatRequest { SessionId = key.SessionId }, Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(Guard, Token);
            Assert.Equal(new long[] { 1 }, callback.Sequences.ToArray());
            Assert.False((await gate.EvaluateAsync(business, Token)).Allowed);
        }
        finally { release.TrySetResult(); }

        Assert.Equal(GameHeartbeatStatus.Ok, (await replay.WaitAsync(Guard, Token)).Status);
        Assert.True((await gate.EvaluateAsync(business, Token)).Allowed);
        Assert.Equal(new long[] { 1, 2 }, callback.Sequences.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_replay_keeps_live_delivery_on_hold_until_retry(bool cancel)
    {
        var sessions = new InMemoryGameSessionRegistry();
        var key = await ResumeAsync(sessions);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var attempts = 0;
        var callback = new RecordingCallback
        {
            BeforeSend = (_, ct) =>
            {
                if (++attempts == 1)
                {
                    if (cancel)
                    {
                        cancellation.Cancel();
                        ct.ThrowIfCancellationRequested();
                    }
                    throw new IOException("Replay write failed.");
                }
                return default;
            }
        };
        await using var connection = new TestCallbackConnection(sessions, "resumed", callback);
        var runtime = Runtime(sessions, connection, new InMemoryReliablePushOutbox(new ReliablePushOptions()));
        var heartbeat = new GameHeartbeatService(sessions, runtime);
        var request = new GameHeartbeatRequest { SessionId = key.SessionId };
        Assert.Equal(ClientNotificationStatus.Retained, await runtime.PublishAsync(key, Command(key), Token));

        var error = await Record.ExceptionAsync(() => heartbeat.HeartbeatAsync("resumed", request, cancellation.Token).AsTask());
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.True(await sessions.IsReliableReplayPendingAsync(key, Token));
        Assert.Empty(callback.Sequences);
        Assert.Equal(ClientNotificationStatus.Retained, await runtime.PublishAsync(key, Command(key), Token));
        Assert.Equal(GameHeartbeatStatus.Ok, (await heartbeat.HeartbeatAsync("resumed", request, Token)).Status);
        Assert.Equal(new long[] { 1, 2 }, callback.Sequences.ToArray());
        Assert.False(await sessions.IsReliableReplayPendingAsync(key, Token));
    }

    [Fact]
    public async Task Heartbeat_finishing_after_live_takeover_cannot_open_the_replacement_binding()
    {
        var sessions = new InMemoryGameSessionRegistry();
        var key = await ResumeAsync(sessions);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalCallback = new RecordingCallback
        {
            BeforeSend = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var outbox = new InMemoryReliablePushOutbox(new ReliablePushOptions());
        await using var original = new TestCallbackConnection(sessions, "resumed", originalCallback);
        var originalRuntime = Runtime(sessions, original, outbox);
        Assert.Equal(ClientNotificationStatus.Retained, await originalRuntime.PublishAsync(key, Command(key), Token));
        var request = new GameHeartbeatRequest { SessionId = key.SessionId };
        var stale = new GameHeartbeatService(sessions, originalRuntime).HeartbeatAsync("resumed", request, Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(Guard, Token);
            await sessions.PrepareProfiledSessionBindingAsync(key, "replacement", "json", Token);
            await sessions.CommitSessionBindingAsync(key, "replacement", Token);
        }
        finally { release.TrySetResult(); }

        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.WaitAsync(Guard, Token));
        Assert.True(await sessions.IsReliableReplayPendingAsync(key, Token));

        var nextCallback = new RecordingCallback();
        await using var replacement = new TestCallbackConnection(sessions, "replacement", nextCallback);
        var nextRuntime = Runtime(sessions, replacement, outbox);
        Assert.Equal(ClientNotificationStatus.Retained, await nextRuntime.PublishAsync(key, Command(key), Token));
        var reply = await new GameHeartbeatService(sessions, nextRuntime).HeartbeatAsync("replacement", request, Token);
        Assert.Equal(GameHeartbeatStatus.Ok, reply.Status);
        Assert.Equal(new long[] { 1, 2 }, nextCallback.Sequences.ToArray());
        Assert.False(await sessions.IsReliableReplayPendingAsync(key, Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_outbox_releases_the_current_binding_on_recovery_heartbeat(bool disconnectFirst)
    {
        var sessions = new InMemoryGameSessionRegistry();
        var key = await ResumeAsync(sessions, disconnectFirst);
        var callback = new RecordingCallback();
        await using var connection = new TestCallbackConnection(sessions, "resumed", callback);
        var runtime = Runtime(sessions, connection, new InMemoryReliablePushOutbox(new ReliablePushOptions()));
        var heartbeat = new GameHeartbeatService(sessions, runtime);

        var reply = await heartbeat.HeartbeatAsync("resumed", new GameHeartbeatRequest { SessionId = key.SessionId }, Token);
        Assert.Equal(GameHeartbeatStatus.Ok, reply.Status);
        Assert.False(await sessions.IsReliableReplayPendingAsync(key, Token));
        Assert.Equal(ClientNotificationStatus.Accepted, await runtime.PublishAsync(key, Command(key), Token));
        Assert.Equal(new long[] { 1 }, callback.Sequences.ToArray());
    }

    private static ReliablePushRuntime Runtime(
        InMemoryGameSessionRegistry sessions, TestCallbackConnection connection, IReliablePushOutbox outbox) =>
        new(outbox, new ReliablePushAckService(outbox),
            new LocalClientNotificationCommandDispatcher(connection.Resolver, sessions), sessions,
            TestNotificationProfiles.JsonCommands());

    private static ClientNotificationCommand Command(GameSessionKey key) => new()
    {
        OwnerKey = key.OwnerKey, SessionId = key.SessionId, SerializerProfile = "json",
        CallbackContractType = typeof(RecordingCallback).AssemblyQualifiedName!,
        ServiceId = 1, MethodId = 2, MethodName = "Notify", Payload = [42]
    };

    private sealed class RecordingCallback : IRpcNotificationDispatchTarget
    {
        public ConcurrentQueue<long> Sequences { get; } = new();
        public Func<long, CancellationToken, ValueTask>? BeforeSend { get; init; }
        public ValueTask DispatchNotificationAsync<T>(int serviceId, int methodId, T payload,
            RpcPushMetadata? metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask DispatchEncodedNotificationAsync(int serviceId, int methodId, ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata, CancellationToken cancellationToken = default)
        {
            var sequence = LakonaInternalCodec.DecodeReliablePushMetadata(metadata!.Payload).Sequence.Value;
            if (BeforeSend is not null) await BeforeSend(sequence, cancellationToken);
            Sequences.Enqueue(sequence);
        }
    }

    // The real outbox owns allocation, retention, locking and replay. Only entry to one operation is delayed.
    private sealed class PausedOutbox : IReliablePushOutbox
    {
        private readonly InMemoryReliablePushOutbox _inner = new(new ReliablePushOptions());
        public bool? PauseReplay { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<long> PublishAsync(string ownerKey, string kind, object payload,
            Func<ReliablePushRecord, ValueTask> deliver, CancellationToken cancellationToken = default)
        {
            if (PauseReplay == false) await PauseAsync(cancellationToken);
            return await _inner.PublishAsync(ownerKey, kind, payload, deliver, cancellationToken);
        }

        public async ValueTask ReplayPendingAsync(string ownerKey, Func<ReliablePushRecord, ValueTask> deliver,
            Func<ValueTask>? onReplayed = null, CancellationToken cancellationToken = default)
        {
            if (PauseReplay == true) await PauseAsync(cancellationToken);
            await _inner.ReplayPendingAsync(ownerKey, deliver, onReplayed, cancellationToken);
        }

        private async Task PauseAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
        }

        public ValueTask AckAsync(string ownerKey, long sequence, CancellationToken cancellationToken = default)
            => _inner.AckAsync(ownerKey, sequence, cancellationToken);
        public ValueTask RemoveAsync(string ownerKey, CancellationToken cancellationToken = default)
            => _inner.RemoveAsync(ownerKey, cancellationToken);
        public long GetLastSequence(string ownerKey) => _inner.GetLastSequence(ownerKey);
    }
}
