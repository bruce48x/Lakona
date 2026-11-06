using System.Threading.Channels;
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Tests;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class ReliablePushCursorRecoveryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly RpcNotificationMethod<int> Notification = new(42, 2);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task A_transient_save_failure_retries_the_highest_admitted_sequence_without_another_push(int lastSequence)
    {
        var ct = TestContext.Current.CancellationToken;
        var failFirst = GameClientTestTransport.Signal();
        var retryEntered = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRetry = GameClientTestTransport.Signal();
        var store = new CursorStore(async (attempt, sequence) =>
        {
            if (attempt == 1)
            {
                await failFirst.Task;
                throw new IOException("Temporary cursor write failure.");
            }
            retryEntered.TrySetResult(sequence);
            await allowRetry.Task;
        });
        var peer = new GameClientTestTransport();
        var deliveries = Channel.CreateUnbounded<int>();
        await using var client = CreateClient(new LakonaGameClientOptions(peer, new IntegerSerializer())
            { ReliablePushCursorStore = store }, deliveries);
        await EstablishAsync(client, peer, ct);
        try
        {
            PushReliable(peer, 1);
            await store.FirstSaveEntered.Task.WaitAsync(Deadline, ct);
            for (var i = 2; i <= lastSequence; i++) PushReliable(peer, i);
            Assert.Equal(Enumerable.Range(1, lastSequence), await DrainDeliveriesAsync(peer, deliveries, ct));
            failFirst.SetResult();

            Assert.Equal(lastSequence, await retryEntered.Task.WaitAsync(Deadline, ct));
            Assert.False(peer.ReliableAcknowledgements.Reader.TryPeek(out _));
            allowRetry.SetResult();
            var ack = await ReadAsync(peer.ReliableAcknowledgements, ct);
            Assert.Equal("session", ack.SessionId);
            Assert.Equal(lastSequence, ack.Sequence.Value);
            Assert.Equal(lastSequence, store.SavedSequence);
            Assert.Empty(await DrainDeliveriesAsync(peer, deliveries, ct));
            Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
            Assert.Equal(LakonaGameConnectionState.Connected, client.ConnectionState);
        }
        finally
        {
            failFirst.TrySetResult();
            allowRetry.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persistent_save_failure_ends_the_connection_with_the_storage_error_and_no_ack(bool duringRecovery)
    {
        var ct = TestContext.Current.CancellationToken;
        var error = new IOException("Cursor storage unavailable.");
        var store = new CursorStore((_, _) => throw error);
        var first = new GameClientTestTransport();
        var releaseHeartbeat = GameClientTestTransport.Signal();
        var second = new GameClientTestTransport { HeartbeatRelease = releaseHeartbeat };
        var peers = new Queue<GameClientTestTransport>([first, second]);
        var scheduler = new ControlledRecoveryScheduler();
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalEvents = 0;
        await using var client = new LakonaGameClientLifecycle(new LakonaGameClientOptions(() => peers.Dequeue(), new IntegerSerializer())
            { ReliablePushCursorStore = store, RecoveryScheduler = scheduler });
        client.Disconnected += cause =>
        {
            Interlocked.Increment(ref terminalEvents);
            disconnected.TrySetResult(cause);
        };
        await EstablishAsync(client, first, ct);
        var failingPeer = duringRecovery ? second : first;
        try
        {
            if (duringRecovery)
            {
                first.Disconnect();
                await scheduler.Waiting.Task.WaitAsync(Deadline, ct);
                scheduler.Step();
                await second.HeartbeatEntered.Task.WaitAsync(Deadline, ct);
                Assert.Equal(LakonaGameConnectionState.Reconnecting, client.ConnectionState);
            }
            PushReliable(failingPeer, 1);

            Assert.Same(error, await disconnected.Task.WaitAsync(Deadline, ct));
            releaseHeartbeat.TrySetResult();
            await failingPeer.Disposed.Task.WaitAsync(Deadline, ct);
            Assert.Equal(1, terminalEvents);
            Assert.Equal(LakonaGameConnectionState.Disconnected, client.ConnectionState);
            Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
            Assert.Equal(ClientConnectionFailureKind.ReliablePushCursorSaveFailed, client.Snapshot.Failure!.Kind);
            Assert.Contains(error.Message, client.Snapshot.Failure.Message);
            Assert.Throws<InvalidOperationException>(client.EnsureApiReady);
            Assert.Equal(3, store.Attempts);
            Assert.Equal(0, store.SavedSequence);
            Assert.False(failingPeer.ReliableAcknowledgements.Reader.TryPeek(out _));
        }
        finally { releaseHeartbeat.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_after_reconnect_skips_admitted_callbacks_and_only_the_replacement_acknowledges(bool retireDuringLastAttempt)
    {
        var ct = TestContext.Current.CancellationToken;
        var oldSaveEntered = GameClientTestTransport.Signal();
        var releaseOldSave = GameClientTestTransport.Signal();
        var newSaveEntered = GameClientTestTransport.Signal();
        var releaseNewSave = GameClientTestTransport.Signal();
        var store = new CursorStore(async (attempt, _) =>
        {
            var lastOldAttempt = retireDuringLastAttempt ? 3 : 1;
            if (attempt < lastOldAttempt) throw new IOException("Temporary old connection save failure.");
            if (attempt == lastOldAttempt)
            {
                oldSaveEntered.TrySetResult();
                // The store ignores cancellation and finishes after its connection is gone.
                await releaseOldSave.Task;
                if (retireDuringLastAttempt) throw new IOException("Retired connection's final write failed.");
                return;
            }
            newSaveEntered.TrySetResult();
            await releaseNewSave.Task;
        });
        var first = new GameClientTestTransport();
        var second = new GameClientTestTransport();
        var peers = new Queue<GameClientTestTransport>([first, second]);
        var scheduler = new ControlledRecoveryScheduler();
        var deliveries = Channel.CreateUnbounded<int>();
        await using var client = CreateClient(new LakonaGameClientOptions(() => peers.Dequeue(), new IntegerSerializer())
            { ReliablePushCursorStore = store, RecoveryScheduler = scheduler }, deliveries);
        var recovered = GameClientTestTransport.Signal();
        var terminalEvents = 0;
        client.Disconnected += _ => Interlocked.Increment(ref terminalEvents);
        client.ConnectionStateChanged += change =>
        {
            if (change.PreviousState == LakonaGameConnectionState.Reconnecting && change.CurrentState == LakonaGameConnectionState.Connected)
                recovered.TrySetResult();
        };
        await EstablishAsync(client, first, ct);
        try
        {
            PushReliable(first, 1);
            await oldSaveEntered.Task.WaitAsync(Deadline, ct);
            Assert.Equal(new[] { 1 }, await DrainDeliveriesAsync(first, deliveries, ct));
            first.Disconnect();
            await scheduler.Waiting.Task.WaitAsync(Deadline, ct);
            scheduler.Step();
            await recovered.Task.WaitAsync(Deadline, ct);
            Assert.Equal("ticket", second.Hello!.ResumeTicket);
            PushReliable(second, 1);
            PushReliable(second, 2);
            Assert.Equal(new[] { 2 }, await DrainDeliveriesAsync(second, deliveries, ct));
            releaseOldSave.SetResult();

            await newSaveEntered.Task.WaitAsync(Deadline, ct);
            Assert.False(first.ReliableAcknowledgements.Reader.TryPeek(out _));
            Assert.False(second.ReliableAcknowledgements.Reader.TryPeek(out _));
            releaseNewSave.SetResult();
            var ack = await ReadAsync(second.ReliableAcknowledgements, ct);
            if (ack.Sequence.Value == 1) ack = await ReadAsync(second.ReliableAcknowledgements, ct);
            Assert.Equal("session", ack.SessionId);
            Assert.Equal(2, ack.Sequence.Value);
            Assert.Equal(2, store.SavedSequence);
            Assert.False(first.ReliableAcknowledgements.Reader.TryPeek(out _));
            Assert.Empty(await DrainDeliveriesAsync(second, deliveries, ct));
            Assert.Equal(0, terminalEvents);
            Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
            Assert.Equal(LakonaGameConnectionState.Connected, client.ConnectionState);
        }
        finally
        {
            releaseOldSave.TrySetResult();
            releaseNewSave.TrySetResult();
        }
    }

    [Fact]
    public async Task Manual_receipt_reports_cursor_failure_while_the_business_callback_is_still_running()
    {
        var ct = TestContext.Current.CancellationToken;
        var error = new IOException("Manual cursor unavailable.");
        var store = new CursorStore((_, _) => throw error);
        await using var core = new LakonaGameClientCore(store);
        core.StartSession("session");
        var callbackEntered = GameClientTestTransport.Signal();
        var releaseCallback = GameClientTestTransport.Signal();
        var acks = 0;
        var processing = core.ProcessReliablePushAsync(ReliablePushSequence.From(1), 1,
            (_, _) =>
            {
                callbackEntered.TrySetResult();
                return new ValueTask(releaseCallback.Task);
            },
            (_, _) =>
            {
                Interlocked.Increment(ref acks);
                return new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted());
            }, ct).AsTask();
        try
        {
            await callbackEntered.Task.WaitAsync(Deadline, ct);
            await WaitForCursorFailureAsync(core, ct);
            Assert.False(processing.IsCompleted);
        }
        finally { releaseCallback.TrySetResult(); }
        var thrown = await Assert.ThrowsAsync<IOException>(() => processing.WaitAsync(Deadline, ct));
        Assert.Same(error, thrown);
        Assert.Equal(3, store.Attempts);
        Assert.Equal(0, acks);
    }

    [Fact]
    public async Task A_terminal_bound_core_drops_reliable_frames_without_blocking_ordinary_rpc_responses()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new CursorStore((_, _) => throw new IOException("Bound cursor unavailable."));
        await using var core = new LakonaGameClientCore(store);
        core.StartSession("session");
        var peer = new GameClientTestTransport();
        await using var rpc = new RpcClientRuntime(peer, new IntegerSerializer());
        core.BindReliablePush(rpc);
        var deliveries = Channel.CreateUnbounded<int>();
        rpc.RegisterNotificationHandler<int>(Notification, value =>
        {
            deliveries.Writer.TryWrite(value);
            return default;
        });
        await rpc.StartAsync(ct);
        PushReliable(peer, 1);
        await WaitForCursorFailureAsync(core, ct);

        PushReliable(peer, 2);
        var echo = await rpc.CallAsync(new RpcMethod<int, int>(42, 1), 17, ct).AsTask().WaitAsync(Deadline, ct);
        Assert.Equal(17, echo);
        Assert.Equal(new[] { 1 }, await DrainDeliveriesAsync(peer, deliveries, ct));
        Assert.Equal(3, store.Attempts);
        Assert.False(peer.ReliableAcknowledgements.Reader.TryPeek(out _));
    }

    private static async Task WaitForCursorFailureAsync(LakonaGameClientCore core, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Deadline);
        while (core.Snapshot.Phase != ClientSessionPhase.ConnectionFailed)
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        Assert.Equal(ClientConnectionFailureKind.ReliablePushCursorSaveFailed, core.Snapshot.Failure!.Kind);
    }

    private static LakonaGameClientLifecycle CreateClient(LakonaGameClientOptions options, Channel<int> deliveries) =>
        new(options, rpc => rpc.RegisterNotificationHandler<int>(Notification, value =>
        {
            deliveries.Writer.TryWrite(value);
            return default;
        }));

    private static async Task EstablishAsync(LakonaGameClientLifecycle client, GameClientTestTransport peer, CancellationToken ct)
    {
        await client.ConnectAsync(ct);
        peer.EstablishSession();
        await peer.EstablishedAcknowledged.Task.WaitAsync(Deadline, ct);
    }

    private static void PushReliable(GameClientTestTransport peer, int sequence) =>
        peer.Push(Notification.ServiceId, Notification.MethodId, BitConverter.GetBytes(sequence), new RpcPushMetadata
        {
            Type = LakonaInternalCodec.ReliablePushMetadataType,
            Payload = LakonaInternalCodec.EncodeReliablePushMetadata(
                new ReliablePushMetadata("session", ReliablePushSequence.From(sequence), "test"))
        });

    private static async Task<List<int>> DrainDeliveriesAsync(GameClientTestTransport peer, Channel<int> deliveries, CancellationToken ct)
    {
        // An ordinary notification sent after the reliable frames is an ordered dispatch barrier.
        peer.Push(Notification.ServiceId, Notification.MethodId, BitConverter.GetBytes(0));
        var values = new List<int>();
        while (true)
        {
            var value = await ReadAsync(deliveries, ct);
            if (value == 0) return values;
            values.Add(value);
        }
    }

    private static Task<T> ReadAsync<T>(Channel<T> channel, CancellationToken ct) =>
        channel.Reader.ReadAsync(ct).AsTask().WaitAsync(Deadline, ct);

    private sealed class CursorStore(Func<int, long, ValueTask> save) : IReliablePushCursorStore
    {
        private int _attempts;
        private long _savedSequence;
        public TaskCompletionSource FirstSaveEntered { get; } = GameClientTestTransport.Signal();
        public int Attempts => Volatile.Read(ref _attempts);
        public long SavedSequence => Volatile.Read(ref _savedSequence);

        public ValueTask<long> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => new(SavedSequence);

        public async ValueTask SaveAsync(string sessionId, long sequence, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            FirstSaveEntered.TrySetResult();
            await save(attempt, sequence);
            Volatile.Write(ref _savedSequence, sequence);
        }

        public ValueTask ClearAsync(string sessionId, CancellationToken cancellationToken = default) => default;
    }
}
