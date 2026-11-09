using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionBindingExpirationTests
{
    public static IEnumerable<object[]> BoundaryCases =>
        from entry in new[] { "bind", "prepare", "profiled", "endpoint" }
        from offset in new[] { -1L, 0L, 1L }
        select new object[] { entry, offset };

    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public async Task Every_binding_entry_obeys_the_exact_resume_deadline(string entry, long offsetTicks)
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ControlledClock();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", ct);
        await BindAsync(registry, session, "old", entry, ct);
        await registry.MarkConnectionDisconnectedAsync("old", ct);
        time.Advance(TimeSpan.FromSeconds(60) + TimeSpan.FromTicks(offsetTicks));

        if (offsetTicks < 0)
        {
            Assert.Equal(SessionResumeStatus.Resumed, (await registry.TryResumeAsync(session, ct)).Status);
            await BindAsync(registry, session, "new", entry, ct);
            Assert.Equal(session, await registry.GetCurrentSessionAsync("new", ct));
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.Empty(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct));
        }
        else
        {
            Assert.Equal(SessionResumeStatus.StateLost, (await registry.TryResumeAsync(session, ct)).Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => BindAsync(registry, session, "new", entry, ct));
            Assert.Null(await registry.GetConnectionIdAsync(session, ct));
            Assert.Null(await registry.GetCurrentSessionAsync("new", ct));
            Assert.Equal(SessionResumeStatus.StateLost, (await registry.TryResumeAsync(session, ct)).Status);
            // A rejected attempt must not reserve the connection or postpone cleanup.
            var other = await registry.StartNewSessionAsync("other", ct);
            await BindAsync(registry, other, "new", entry, ct);
            Assert.Equal(session, Assert.Single(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct)).Session);
            Assert.Equal(other, await registry.GetCurrentSessionAsync("new", ct));
        }
        Assert.Null(await registry.GetCurrentSessionAsync("old", ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_bind_cannot_revive_an_expired_session_before_cleanup(bool reliable)
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ControlledClock();
        var notifier = new FirstEstablishmentGate();
        notifier.Acknowledge.SetResult();
        await using var provider = CreateProvider(time, notifier);
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var registry = provider.GetRequiredService<IGameSessionRegistry>();
        var policies = provider.GetRequiredService<GameConnectionDeliveryPolicyRegistry>();
        policies.Set("old", reliable, "endpoint", "json");
        policies.Set("new", reliable, "endpoint", "json");
        var session = await server.StartSessionAsync("owner", "old", ct);
        await server.MarkSessionDisconnectedAsync(session, "old", ct);
        time.Advance(TimeSpan.FromSeconds(61));

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.BindSessionAsync(session, "new", ct).AsTask());
        Assert.Equal(SessionResumeStatus.StateLost, (await registry.TryResumeAsync(session, ct)).Status);
        Assert.Null(await registry.GetCurrentSessionAsync("new", ct));
        Assert.Null(await registry.GetConnectionIdAsync(session, ct));
        Assert.Single(notifier.Notifications);
        Assert.Equal(session, Assert.Single(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct)).Session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Binding_and_cleanup_contend_at_the_locked_deadline_check(bool alreadyExpired)
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ControlledClock();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", ct);
        await registry.BindSessionAsync(session, "old", ct);
        await registry.MarkConnectionDisconnectedAsync("old", ct);
        time.Advance(TimeSpan.FromSeconds(alreadyExpired ? 60 : 59));
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        time.BeforeNextRead = () =>
        {
            checking.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10), ct)) throw new TimeoutException("Binding gate was not released.");
        };
        var bind = Task.Run(async () => await registry.BindSessionAsync(session, "new", ct), ct);
        Task<IReadOnlyList<GameSessionExpiration>>? cleanup = null;
        try
        {
            await checking.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            time.Advance(TimeSpan.FromSeconds(2));
            var cleanupTime = time.GetUtcNow();
            cleanup = Task.Run(async () =>
            {
                cleaning.TrySetResult();
                return await registry.ExpireSessionsAsync(cleanupTime, ct);
            }, ct);
            await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(cleanup.IsCompleted);
        }
        finally { release.Set(); }

        Assert.NotNull(cleanup);
        if (alreadyExpired)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => bind);
            Assert.Equal(session, Assert.Single(await cleanup.WaitAsync(TimeSpan.FromSeconds(5), ct)).Session);
            Assert.Null(await registry.GetCurrentSessionAsync("new", ct));
            Assert.Equal(SessionResumeStatus.StateLost, (await registry.TryResumeAsync(session, ct)).Status);
        }
        else
        {
            await bind.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Empty(await cleanup.WaitAsync(TimeSpan.FromSeconds(5), ct));
            Assert.Equal(session, await registry.GetCurrentSessionAsync("new", ct));
        }
    }

    [Fact]
    public async Task Binding_after_cleanup_cannot_leave_a_connection_mapping()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ControlledClock();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", ct);
        await registry.BindSessionAsync(session, "old", ct);
        await registry.MarkConnectionDisconnectedAsync("old", ct);
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Single(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.BindSessionAsync(session, "new", ct).AsTask());
        var other = await registry.StartNewSessionAsync("other", ct);
        await registry.BindSessionAsync(other, "new", ct);
        Assert.Equal(other, await registry.GetCurrentSessionAsync("new", ct));
    }

    [Fact]
    public async Task Reservation_before_deadline_can_commit_after_deadline()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ControlledClock();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", ct);
        await registry.BindSessionAsync(session, "old", ct);
        await registry.MarkConnectionDisconnectedAsync("old", ct);
        time.Advance(TimeSpan.FromSeconds(59));
        await registry.PrepareSessionBindingAsync(session, "new", ct);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct));
        await registry.CommitSessionBindingAsync(session, "new", ct);
        Assert.Equal(session, await registry.GetCurrentSessionAsync("new", ct));
        Assert.Equal(SessionResumeStatus.Resumed, (await registry.TryResumeAsync(session, ct)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_canceled_commit_rolls_back_to_the_original_expired_deadline(bool canceled)
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ControlledClock();
        var registry = CreateRegistry(time);
        var session = await registry.StartNewSessionAsync("owner", ct);
        await registry.BindSessionAsync(session, "old", ct);
        await registry.MarkConnectionDisconnectedAsync("old", ct);
        time.Advance(TimeSpan.FromSeconds(59));
        await registry.PrepareSessionBindingAsync(session, "new", ct);
        time.Advance(TimeSpan.FromSeconds(2));
        if (canceled)
        {
            using var caller = new CancellationTokenSource();
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.CommitSessionBindingAsync(session, "new", caller.Token).AsTask());
        }
        else
        {
            await registry.MarkConnectionDisconnectedAsync("new", ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.CommitSessionBindingAsync(session, "new", ct).AsTask());
        }
        await registry.RollbackSessionBindingAsync(session, "new", ct);
        Assert.Null(await registry.GetCurrentSessionAsync("old", ct));
        Assert.Null(await registry.GetCurrentSessionAsync("new", ct));
        Assert.Equal(SessionResumeStatus.StateLost, (await registry.TryResumeAsync(session, ct)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.BindSessionAsync(session, "retry", ct).AsTask());
        var expired = Assert.Single(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct));
        Assert.Equal(session, expired.Session);
        Assert.Equal("old", expired.ConnectionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_delayed_binding_failure_or_cancellation_remains_retryable(bool canceled)
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new ControlledClock();
        var notifier = new FirstEstablishmentGate();
        await using var provider = CreateProvider(time, notifier);
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var registry = provider.GetRequiredService<IGameSessionRegistry>();
        var tickets = provider.GetRequiredService<IGameSessionResumeTicketStore>();
        var policies = provider.GetRequiredService<GameConnectionDeliveryPolicyRegistry>();
        policies.Set("first", false, "plain", "json");
        policies.Set("retry", true, "reliable", "binary");
        var session = await server.StartSessionAsync("owner", ct);
        await registry.SetSessionItemAsync(session, "room", GameSessionItemValue.FromString("room-1"), ct);
        time.Advance(TimeSpan.FromHours(1));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var bind = server.BindSessionAsync(session, "first", caller.Token).AsTask();
        var firstNotice = await notifier.First.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        try
        {
            time.Advance(TimeSpan.FromHours(1));
            Assert.Empty(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct));
            if (canceled)
            {
                caller.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bind);
            }
            else
            {
                notifier.Acknowledge.TrySetException(new IOException("Establishment failed."));
                await Assert.ThrowsAsync<IOException>(() => bind);
            }
        }
        finally { notifier.Acknowledge.TrySetResult(); }
        Assert.Null(await registry.GetCurrentSessionAsync("first", ct));
        Assert.Null(await tickets.ResolveAsync(firstNotice.ResumeTicket, "plain", ct));
        time.Advance(TimeSpan.FromHours(1));
        Assert.Empty(await registry.ExpireSessionsAsync(time.GetUtcNow(), ct));
        await server.BindSessionAsync(session, "retry", ct);
        Assert.Equal(session, await registry.GetCurrentSessionAsync("retry", ct));
        Assert.True(await registry.GetReliablePushPolicyAsync(session, ct));
        Assert.Equal("binary", await registry.GetNotificationSerializerProfileAsync(session, ct));
        Assert.Equal("room-1", (await registry.GetSessionItemAsync(session, "room", ct))?.GetString());
        Assert.Equal(2, notifier.Notifications.Count);
    }

    private static async Task BindAsync(InMemoryGameSessionRegistry registry, GameSessionKey session,
        string connection, string entry, CancellationToken ct)
    {
        switch (entry)
        {
            case "bind": await registry.BindSessionAsync(session, connection, ct); return;
            case "prepare": await registry.PrepareSessionBindingAsync(session, connection, ct); break;
            case "profiled": await registry.PrepareProfiledSessionBindingAsync(session, connection, "json", ct); break;
            case "endpoint": await registry.PrepareEndpointSessionBindingAsync(session, connection, true, "json", ct); break;
            default: throw new ArgumentOutOfRangeException(nameof(entry));
        }
        await registry.CommitSessionBindingAsync(session, connection, ct);
    }

    private static InMemoryGameSessionRegistry CreateRegistry(TimeProvider time) => new(new LakonaGameHostingOptions(), time);

    private static ServiceProvider CreateProvider(TimeProvider time, IGameSessionEstablishedNotifier notifier)
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        services.AddSingleton(time);
        services.AddSingleton(notifier);
        return services.BuildServiceProvider();
    }

    private sealed class ControlledClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero).Ticks;
        public Action? BeforeNextRead;
        public override DateTimeOffset GetUtcNow()
        {
            var captured = new DateTimeOffset(Interlocked.Read(ref ticks), TimeSpan.Zero);
            Interlocked.Exchange(ref BeforeNextRead, null)?.Invoke();
            return captured;
        }
        public void Advance(TimeSpan amount) => Interlocked.Add(ref ticks, amount.Ticks);
    }
}
