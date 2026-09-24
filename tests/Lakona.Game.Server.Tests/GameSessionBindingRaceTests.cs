using Lakona.Game.Abstractions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionBindingRaceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Rollback_preserves_disconnect_during_prepare_and_its_original_deadline(
        bool disconnectBySession, bool sameConnection)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", cancellationToken);
        await registry.BindSessionAsync(session, "old", cancellationToken);
        var replacement = sameConnection ? "old" : "new";
        await registry.PrepareSessionBindingAsync(session, replacement, cancellationToken);

        if (disconnectBySession)
            await registry.MarkSessionDisconnectedAsync(session, "old", cancellationToken);
        else
            Assert.NotNull(await registry.MarkConnectionDisconnectedAsync("old", cancellationToken));

        Assert.Null(await registry.GetConnectionIdAsync(session, cancellationToken));
        Assert.Null(await registry.MarkConnectionDisconnectedAsync("old", cancellationToken));
        time.Advance(TimeSpan.FromSeconds(40));
        await registry.RollbackSessionBindingAsync(session, replacement, cancellationToken);

        Assert.Null(await registry.GetConnectionIdAsync(session, cancellationToken));
        Assert.Null(await registry.GetCurrentSessionAsync("old", cancellationToken));
        Assert.Null(await registry.GetCurrentSessionAsync(replacement, cancellationToken));
        Assert.Equal(1, registry.GetDiagnosticsSnapshot().DisconnectedSessions);
        time.Advance(TimeSpan.FromSeconds(19));
        Assert.Empty(await registry.ExpireSessionsAsync(time.GetUtcNow(), cancellationToken));
        time.Advance(TimeSpan.FromSeconds(1));
        var expired = Assert.Single(await registry.ExpireSessionsAsync(time.GetUtcNow(), cancellationToken));
        Assert.Equal(session, expired.Session);
        Assert.Equal("old", expired.ConnectionId);
    }

    [Fact]
    public async Task Old_disconnect_during_prepare_does_not_disconnect_committed_replacement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var registry = CreateRegistry(new ManualTimeProvider());
        var session = await registry.StartNewSessionAsync("owner", cancellationToken);
        await registry.BindSessionAsync(session, "old", cancellationToken);
        await registry.PrepareSessionBindingAsync(session, "new", cancellationToken);
        Assert.Null(await registry.GetCurrentSessionAsync("old", cancellationToken));
        Assert.Null(await registry.GetCurrentSessionAsync("new", cancellationToken));
        Assert.NotNull(await registry.MarkConnectionDisconnectedAsync("old", cancellationToken));
        await registry.CommitSessionBindingAsync(session, "new", cancellationToken);

        Assert.Null(await registry.MarkConnectionDisconnectedAsync("old", cancellationToken));
        Assert.Equal("new", await registry.GetConnectionIdAsync(session, cancellationToken));
        Assert.Equal(session, await registry.GetCurrentSessionAsync("new", cancellationToken));
        Assert.NotNull(await registry.TakeResumedSessionAsync(session, "new", cancellationToken));
        Assert.Null(await registry.TakeResumedSessionAsync(session, "new", cancellationToken));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Replacement_disconnect_rejects_commit_and_rollback_uses_latest_old_state(
        bool oldAlsoDisconnects, bool oldDisconnectsFirst)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var registry = CreateRegistry(new ManualTimeProvider());
        var session = await registry.StartNewSessionAsync("owner", cancellationToken);
        await registry.BindSessionAsync(session, "old", cancellationToken);
        await registry.PrepareSessionBindingAsync(session, "new", cancellationToken);
        if (oldAlsoDisconnects && oldDisconnectsFirst)
            Assert.NotNull(await registry.MarkConnectionDisconnectedAsync("old", cancellationToken));
        Assert.NotNull(await registry.MarkConnectionDisconnectedAsync("new", cancellationToken));
        if (oldAlsoDisconnects && !oldDisconnectsFirst)
            Assert.NotNull(await registry.MarkConnectionDisconnectedAsync("old", cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.CommitSessionBindingAsync(session, "new", cancellationToken).AsTask());
        await registry.RollbackSessionBindingAsync(session, "new", cancellationToken);
        Assert.Equal(oldAlsoDisconnects ? null : "old", await registry.GetConnectionIdAsync(session, cancellationToken));
        Assert.Null(await registry.GetCurrentSessionAsync("new", cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Termination_during_prepare_cannot_be_undone_by_rollback(bool keepForResume)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", cancellationToken);
        await registry.BindSessionAsync(session, "old", cancellationToken);
        await registry.PrepareSessionBindingAsync(session, "new", cancellationToken);
        await registry.MarkSessionTerminatedAsync(session,
            new SessionTerminationNotice(SessionTerminationReason.Policy), keepForResume, cancellationToken);
        await registry.RollbackSessionBindingAsync(session, "new", cancellationToken);

        Assert.Null(await registry.GetConnectionIdAsync(session, cancellationToken));
        Assert.Null(await registry.MarkConnectionDisconnectedAsync("old", cancellationToken));
        Assert.Null(await registry.MarkConnectionDisconnectedAsync("new", cancellationToken));
        Assert.Equal(keepForResume ? SessionResumeStatus.Terminated : SessionResumeStatus.StateLost,
            (await registry.TryResumeAsync(session, cancellationToken)).Status);
        if (keepForResume)
        {
            Assert.Equal(GameSessionHeartbeatStatus.Terminated,
                (await registry.RecordHeartbeatAsync("new", time.GetUtcNow(), cancellationToken)).Status);
            time.Advance(TimeSpan.FromSeconds(60));
            Assert.Single(await registry.ExpireSessionsAsync(time.GetUtcNow(), cancellationToken));
        }
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("remove")]
    [InlineData("expire")]
    [InlineData("terminate")]
    public async Task Old_connection_is_reserved_only_until_the_pending_binding_is_resolved(string completion)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", cancellationToken);
        var other = await registry.StartNewSessionAsync("other", cancellationToken);
        await registry.BindSessionAsync(session, "old", cancellationToken);
        await registry.PrepareSessionBindingAsync(session, "new", cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.BindSessionAsync(other, "old", cancellationToken).AsTask());
        switch (completion)
        {
            case "commit": await registry.CommitSessionBindingAsync(session, "new", cancellationToken); break;
            case "remove": await registry.RemoveSessionAsync(session, cancellationToken); break;
            case "terminate":
                await registry.MarkSessionTerminatedAsync(session,
                    new SessionTerminationNotice(SessionTerminationReason.Policy), true, cancellationToken);
                break;
            case "expire":
                await registry.MarkConnectionDisconnectedAsync("new", cancellationToken);
                time.Advance(TimeSpan.FromSeconds(60));
                Assert.Single(await registry.ExpireSessionsAsync(time.GetUtcNow(), cancellationToken));
                break;
        }
        await registry.BindSessionAsync(other, "old", cancellationToken);
        Assert.Equal(other, await registry.GetCurrentSessionAsync("old", cancellationToken));
    }

    [Fact]
    public async Task Cancelled_commit_can_be_rolled_back_without_resurrecting_old_connection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var registry = CreateRegistry(new ManualTimeProvider());
        var session = await registry.StartNewSessionAsync("owner", cancellationToken);
        await registry.BindSessionAsync(session, "old", cancellationToken);
        await registry.PrepareSessionBindingAsync(session, "new", cancellationToken);
        await registry.MarkConnectionDisconnectedAsync("old", cancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.CommitSessionBindingAsync(session, "new", cancellation.Token).AsTask());
        await registry.RollbackSessionBindingAsync(session, "new", cancellationToken);
        Assert.Null(await registry.GetConnectionIdAsync(session, cancellationToken));
    }

    private static InMemoryGameSessionRegistry CreateRegistry(TimeProvider time) => new(
        new LakonaGameHostingOptions
        {
            Sessions = new LakonaSessionHostingOptions { ResumeWindow = TimeSpan.FromSeconds(60) }
        }, time);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
