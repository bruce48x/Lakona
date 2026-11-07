using Lakona.Game.Server.ReliablePush;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ReliablePushByteBudgetTests
{
    [Fact]
    public async Task Waiting_publication_reserves_bytes_and_cancellation_releases_them_without_sequence()
    {
        var ct = TestContext.Current.CancellationToken;
        var outbox = new InMemoryReliablePushOutbox(new ReliablePushOptions
        { MaxPendingBytesPerSession = 4096, MaxPendingBytesPerProcess = 1024 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = outbox.PublishAsync("a", "active", new byte[200], async _ =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
        }, ct, retainedBytes: 200).AsTask();
        using var canceled = new CancellationTokenSource();
        try
        {
            await entered.Task.WaitAsync(ct);
            var waiting = outbox.PublishAsync("a", "waiting", new byte[400], _ => default,
                canceled.Token, retainedBytes: 400).AsTask();
            Assert.False(waiting.IsCompleted);
            await Assert.ThrowsAsync<ReliablePushContinuityLostException>(() =>
                outbox.PublishAsync("b", "overflow", new byte[200], _ => default, ct, retainedBytes: 200).AsTask());
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(ct));
            Assert.Equal(1, outbox.GetLastSequence("a"));
            await outbox.PublishAsync("c", "fits", new byte[400], _ => default, ct, retainedBytes: 400);
        }
        finally { canceled.Cancel(); release.TrySetResult(); }
        await active;
    }

    [Fact]
    public async Task Session_byte_overflow_preserves_records_and_marks_continuity_lost()
    {
        var outbox = new InMemoryReliablePushOutbox(new ReliablePushOptions
        { MaxPendingBytesPerSession = 1024, MaxPendingBytesPerProcess = 4096 });
        await outbox.PublishAsync("a", "first", new byte[700], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 700);
        var error = await Assert.ThrowsAsync<ReliablePushContinuityLostException>(() =>
            outbox.PublishAsync("a", "overflow", new byte[200], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 200).AsTask());
        Assert.True(error.NewlyLost);
        Assert.Equal(1, outbox.GetLastSequence("a"));
        await outbox.AckAsync("a", 1, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ReliablePushContinuityLostException>(() =>
            outbox.ReplayPendingAsync("a", _ => default, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Process_budget_is_released_by_ack_and_remove()
    {
        var outbox = new InMemoryReliablePushOutbox(new ReliablePushOptions
        { MaxPendingBytesPerSession = 4096, MaxPendingBytesPerProcess = 1024 });
        await outbox.PublishAsync("a", "first", new byte[700], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 700);
        await Assert.ThrowsAsync<ReliablePushContinuityLostException>(() =>
            outbox.PublishAsync("b", "overflow", new byte[200], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 200).AsTask());
        await outbox.AckAsync("a", 1, TestContext.Current.CancellationToken);
        await outbox.PublishAsync("c", "next", new byte[700], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 700);
        await outbox.RemoveAsync("c", TestContext.Current.CancellationToken);
        await outbox.PublishAsync("d", "next", new byte[700], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 700);
    }

    [Fact]
    public async Task Ack_during_active_delivery_keeps_bytes_reserved_until_delivery_finishes()
    {
        var outbox = new InMemoryReliablePushOutbox(new ReliablePushOptions
        { MaxPendingBytesPerSession = 4096, MaxPendingBytesPerProcess = 1024 });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishing = outbox.PublishAsync("a", "first", new byte[700], async _ =>
        {
            entered.SetResult();
            await release.Task;
        }, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 700).AsTask();
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            await outbox.AckAsync("a", 1, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<ReliablePushContinuityLostException>(() =>
                outbox.PublishAsync("b", "overflow", new byte[200], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 200).AsTask());
        }
        finally { release.TrySetResult(); }
        await publishing;
        await outbox.PublishAsync("c", "next", new byte[700], _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: 700);
    }

    [Fact]
    public async Task Failed_delivery_retains_large_payload_for_replay_and_ack_releases_budget()
    {
        var payload = new byte[12 * 1024 * 1024];
        new Random(42).NextBytes(payload);
        var outbox = new InMemoryReliablePushOutbox(new ReliablePushOptions
        { MaxPendingBytesPerSession = payload.Length + 128, MaxPendingBytesPerProcess = payload.Length + 128 });
        await Assert.ThrowsAsync<IOException>(() => outbox.PublishAsync("a", "large", payload,
            _ => throw new IOException("Disconnected"), cancellationToken: TestContext.Current.CancellationToken, retainedBytes: payload.Length).AsTask());
        var replayed = 0;
        await outbox.ReplayPendingAsync("a", record =>
        {
            Assert.Same(payload, record.Payload);
            Assert.Equal(1, record.Sequence);
            replayed++;
            return default;
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, replayed);
        await outbox.AckAsync("a", 1, TestContext.Current.CancellationToken);
        Assert.Equal(2, await outbox.PublishAsync("a", "next", payload, _ => default, cancellationToken: TestContext.Current.CancellationToken, retainedBytes: payload.Length));
    }
}
