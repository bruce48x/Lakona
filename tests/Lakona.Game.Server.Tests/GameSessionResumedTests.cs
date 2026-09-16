using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionResumedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_notifies_once_after_replay_and_contains_handler_failures(bool reliable)
    {
        var sessions = new InMemoryGameSessionRegistry();
        var session = await sessions.StartNewSessionAsync("player", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.SetReliablePushPolicyAsync(session, reliable, cancellationToken: TestContext.Current.CancellationToken);
        var recording = new RecordingHandler();
        var replay = new ReplayRuntime(sessions);
        using var services = new ServiceCollection()
            .AddSingleton<IGameSessionLifecycleHandler>(new ThrowingHandler())
            .AddSingleton<IGameSessionLifecycleHandler>(recording)
            .AddSingleton<IReliablePushRuntime>(replay).BuildServiceProvider();
        var heartbeat = new GameHeartbeatService(sessions, services);
        var request = new GameHeartbeatRequest { SessionId = session.SessionId };
        await sessions.BindSessionAsync(session, "first", cancellationToken: TestContext.Current.CancellationToken);
        await heartbeat.HeartbeatAsync("first", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(recording.Calls);
        await sessions.MarkConnectionDisconnectedAsync("first", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.BindSessionAsync(session, "second", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(recording.Calls);
        replay.Block = true;
        var recovery = heartbeat.HeartbeatAsync("second", request, cancellationToken: TestContext.Current.CancellationToken).AsTask();
        await replay.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Empty(recording.Calls);
        replay.Release.SetResult();
        await recovery;
        await heartbeat.HeartbeatAsync("second", request, cancellationToken: TestContext.Current.CancellationToken);
        var call = Assert.Single(recording.Calls);
        Assert.Equal(session, call.Session);
        Assert.Equal("second", call.ConnectionId);
    }

    [Fact]
    public async Task Recovery_without_reliable_runtime_still_notifies()
    {
        var token = TestContext.Current.CancellationToken;
        var sessions = new InMemoryGameSessionRegistry();
        var session = await sessions.StartNewSessionAsync("player", token);
        var recording = new RecordingHandler();
        using var services = new ServiceCollection().AddSingleton<IGameSessionLifecycleHandler>(recording).BuildServiceProvider();
        var heartbeat = new GameHeartbeatService(sessions, services);
        await sessions.BindSessionAsync(session, "first", token);
        await sessions.MarkConnectionDisconnectedAsync("first", token);
        await sessions.BindSessionAsync(session, "second", token);
        await heartbeat.HeartbeatAsync("second", new GameHeartbeatRequest { SessionId = "other-session" }, token);
        await heartbeat.HeartbeatAsync("second", new GameHeartbeatRequest(), token);
        Assert.Empty(recording.Calls);
        await heartbeat.HeartbeatAsync("second", new GameHeartbeatRequest { SessionId = session.SessionId }, token);
        Assert.Single(recording.Calls);
    }

    [Fact]
    public async Task Failed_binding_stale_connection_and_lost_continuity_do_not_publish_recovery()
    {
        var sessions = new InMemoryGameSessionRegistry();
        var session = await sessions.StartNewSessionAsync("player", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.BindSessionAsync(session, "first", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.MarkConnectionDisconnectedAsync("first", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.PrepareSessionBindingAsync(session, "failed", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(await sessions.TakeResumedSessionAsync(session, "failed", cancellationToken: TestContext.Current.CancellationToken));
        await sessions.RollbackSessionBindingAsync(session, "failed", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(await sessions.TakeResumedSessionAsync(session, "failed", cancellationToken: TestContext.Current.CancellationToken));
        await sessions.BindSessionAsync(session, "second", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(await sessions.TakeResumedSessionAsync(session, "first", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(await sessions.TakeResumedSessionAsync(new GameSessionKey("player", "wrong"), "second", TestContext.Current.CancellationToken));
        await sessions.MarkReliableContinuityLostAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(await sessions.TakeResumedSessionAsync(session, "second", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Connection_replacement_before_old_disconnect_still_resumes_only_new_binding()
    {
        var sessions = new InMemoryGameSessionRegistry();
        var session = await sessions.StartNewSessionAsync("player", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.BindSessionAsync(session, "first", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.BindSessionAsync(session, "second", cancellationToken: TestContext.Current.CancellationToken);
        await sessions.MarkConnectionDisconnectedAsync("first", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(await sessions.TakeResumedSessionAsync(session, "first", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(await sessions.TakeResumedSessionAsync(new GameSessionKey("player", "wrong"), "second", TestContext.Current.CancellationToken));
        Assert.Equal("second", (await sessions.TakeResumedSessionAsync(session, "second", cancellationToken: TestContext.Current.CancellationToken))?.ConnectionId);
        Assert.Null(await sessions.TakeResumedSessionAsync(session, "second", cancellationToken: TestContext.Current.CancellationToken));
    }

    private class RecordingHandler : IGameSessionLifecycleHandler
    {
        public List<GameSessionBindingContext> Calls { get; } = [];
        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default) => default;
        public virtual ValueTask OnSessionResumedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            Calls.Add(context);
            return default;
        }
    }

    private sealed class ThrowingHandler : RecordingHandler
    {
        public override ValueTask OnSessionResumedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default) => throw new InvalidOperationException("hook failure");
    }

    private sealed class ReplayRuntime(IGameSessionRegistry sessions) : IReliablePushRuntime
    {
        public bool Block;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask ReplayPendingAsync(GameSessionKey session, CancellationToken cancellationToken = default)
        {
            if (Block)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            await sessions.MarkReliableReplayReadyAsync(session, cancellationToken);
        }
        public ValueTask<ClientNotificationStatus> PublishAsync(GameSessionKey session, ClientNotificationCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReliablePushAckOutcome> AckAsync(GameSessionKey currentSession, GameSessionKey acknowledgedSession, long sequence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
