using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ReliablePushAckServiceTests
{
    [Fact]
    public async Task AckAcceptedPrunesOnlyMatchingSessionGeneration()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerReliablePush();
        using var provider = services.BuildServiceProvider();
        var outbox = provider.GetRequiredService<IReliablePushOutbox>();
        var ackService = provider.GetRequiredService<IReliablePushAckService>();
        var oldSession = new GameSessionKey("player-a", "session-a", 1);
        var newSession = new GameSessionKey("player-a", "session-b", 2);
        await outbox.PublishAsync(oldSession, "Old", "old", _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);
        await outbox.PublishAsync(newSession, "New", "new", _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        var outcome = await ackService.AckAsync(
            oldSession,
            oldSession,
            1,
            TestContext.Current.CancellationToken);
        var oldReplayed = new List<ReliablePushRecord>();
        var newReplayed = new List<ReliablePushRecord>();
        await outbox.ReplayPendingAsync(oldSession, Capture(oldReplayed), TestContext.Current.CancellationToken);
        await outbox.ReplayPendingAsync(newSession, Capture(newReplayed), TestContext.Current.CancellationToken);

        Assert.Equal(ReliablePushAckStatus.Accepted, outcome.Status);
        Assert.Empty(oldReplayed);
        Assert.Single(newReplayed);
    }

    [Fact]
    public async Task AckForOldGenerationDoesNotPruneCurrentSession()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerReliablePush();
        using var provider = services.BuildServiceProvider();
        var outbox = provider.GetRequiredService<IReliablePushOutbox>();
        var ackService = provider.GetRequiredService<IReliablePushAckService>();
        var current = new GameSessionKey("player-a", "session-b", 2);
        var old = new GameSessionKey("player-a", "session-a", 1);
        await outbox.PublishAsync(current, "New", "new", _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        var outcome = await ackService.AckAsync(
            current,
            old,
            1,
            TestContext.Current.CancellationToken);
        var replayed = new List<ReliablePushRecord>();
        await outbox.ReplayPendingAsync(current, Capture(replayed), TestContext.Current.CancellationToken);

        Assert.Equal(ReliablePushAckStatus.SessionMismatch, outcome.Status);
        Assert.Single(replayed);
    }

    [Fact]
    public void AddReliablePushRegistersAckService()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServerReliablePush();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IReliablePushAckService>());
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
