using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.ReliablePush;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ReliablePushOutboxTests
{
    [Fact]
    public async Task PublishAssignsMonotonicSequencesPerOwnerAndDeliversRecord()
    {
        var outbox = CreateOutbox();
        var delivered = new List<ReliablePushRecord>();
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await outbox.PublishAsync("player-a", "MatchReady", new { RoomId = "room-1" }, Capture(delivered), cancellationToken);
        var second = await outbox.PublishAsync("player-a", "MatchReady", new { RoomId = "room-2" }, Capture(delivered), cancellationToken);
        var otherOwner = await outbox.PublishAsync("player-b", "MatchReady", new { RoomId = "room-3" }, Capture(delivered), cancellationToken);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(1, otherOwner);
        Assert.Equal(2, outbox.GetLastSequence("player-a"));
        Assert.Equal(1, outbox.GetLastSequence("player-b"));
        Assert.Collection(
            delivered,
            record => Assert.Equal(("player-a", 1), (record.OwnerKey, record.Sequence)),
            record => Assert.Equal(("player-a", 2), (record.OwnerKey, record.Sequence)),
            record => Assert.Equal(("player-b", 1), (record.OwnerKey, record.Sequence)));
    }

    [Fact]
    public async Task ReplayPendingReplaysUnacknowledgedRecordsInSequenceOrder()
    {
        var outbox = CreateOutbox();
        var cancellationToken = TestContext.Current.CancellationToken;
        await outbox.PublishAsync("player-a", "First", "one", _ => ValueTask.CompletedTask, cancellationToken);
        await outbox.PublishAsync("player-a", "Second", "two", _ => ValueTask.CompletedTask, cancellationToken);
        await outbox.PublishAsync("player-a", "Third", "three", _ => ValueTask.CompletedTask, cancellationToken);
        await outbox.AckAsync("player-a", 1, cancellationToken);
        var replayed = new List<ReliablePushRecord>();

        await outbox.ReplayPendingAsync("player-a", Capture(replayed), cancellationToken);

        Assert.Collection(
            replayed,
            record => Assert.Equal(("Second", 2), (record.Kind, record.Sequence)),
            record => Assert.Equal(("Third", 3), (record.Kind, record.Sequence)));
    }

    [Fact]
    public async Task AckRemovesAllRecordsUpToSequence()
    {
        var outbox = CreateOutbox();
        var cancellationToken = TestContext.Current.CancellationToken;
        await outbox.PublishAsync("player-a", "First", "one", _ => ValueTask.CompletedTask, cancellationToken);
        await outbox.PublishAsync("player-a", "Second", "two", _ => ValueTask.CompletedTask, cancellationToken);
        await outbox.PublishAsync("player-a", "Third", "three", _ => ValueTask.CompletedTask, cancellationToken);

        await outbox.AckAsync("player-a", 2, cancellationToken);
        var replayed = new List<ReliablePushRecord>();
        await outbox.ReplayPendingAsync("player-a", Capture(replayed), cancellationToken);

        var record = Assert.Single(replayed);
        Assert.Equal(3, record.Sequence);
    }

    [Fact]
    public async Task MaxPendingPerOwnerKeepsNewestRecords()
    {
        var outbox = CreateOutbox(options => options.MaxPendingPerOwner = 2);
        var cancellationToken = TestContext.Current.CancellationToken;
        await outbox.PublishAsync("player-a", "First", "one", _ => ValueTask.CompletedTask, cancellationToken);
        await outbox.PublishAsync("player-a", "Second", "two", _ => ValueTask.CompletedTask, cancellationToken);
        await outbox.PublishAsync("player-a", "Third", "three", _ => ValueTask.CompletedTask, cancellationToken);
        var replayed = new List<ReliablePushRecord>();

        await outbox.ReplayPendingAsync("player-a", Capture(replayed), cancellationToken);

        Assert.Collection(
            replayed,
            record => Assert.Equal(2, record.Sequence),
            record => Assert.Equal(3, record.Sequence));
    }

    [Fact]
    public void AddReliablePushRegistersOptionsAndOutbox()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServerReliablePush(options => options.MaxPendingPerOwner = 7);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(7, provider.GetRequiredService<ReliablePushOptions>().MaxPendingPerOwner);
        Assert.NotNull(provider.GetRequiredService<IReliablePushOutbox>());
    }

    private static IReliablePushOutbox CreateOutbox(Action<ReliablePushOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerReliablePush(configure);
        return services.BuildServiceProvider().GetRequiredService<IReliablePushOutbox>();
    }

    private static Func<ReliablePushRecord, ValueTask> Capture(List<ReliablePushRecord> records)
    {
        return record =>
        {
            records.Add(record);
            return ValueTask.CompletedTask;
        };
    }
}
