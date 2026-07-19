using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameHeartbeatTests
{
    [Fact]
    public void Heartbeat_contract_defaults_and_ids_are_stable()
    {
        var request = new GameHeartbeatRequest();
        var reply = new GameHeartbeatReply();

        Assert.Equal(1, request.ProtocolVersion);
        Assert.Null(request.SessionId);
        Assert.Equal(GameHeartbeatStatus.Ok, reply.Status);
        Assert.Null(reply.Message);
        Assert.Equal(GameHandshakeRpcIds.ServiceId, GameHeartbeatRpcIds.ServiceId);
        Assert.Equal(2, GameHeartbeatRpcIds.HeartbeatMethodId);
    }

    [Fact]
    public async Task Heartbeat_service_maps_unbound_connection_to_ok()
    {
        var directory = new InMemoryGameSessionRegistry();
        var service = new GameHeartbeatService(directory);

        var reply = await service.HeartbeatAsync(
            "connection-a",
            new GameHeartbeatRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(GameHeartbeatStatus.Ok, reply.Status);
    }

    [Fact]
    public async Task Heartbeat_service_maps_terminated_connection_to_terminated()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new object(), TestContext.Current.CancellationToken);
        await directory.MarkSessionTerminatedAsync(
            session,
            new SessionTerminationNotice(SessionTerminationReason.Policy, "removed"),
            keepForResume: true,
            TestContext.Current.CancellationToken);
        var service = new GameHeartbeatService(directory);

        var reply = await service.HeartbeatAsync(
            "connection-a",
            new GameHeartbeatRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(GameHeartbeatStatus.Terminated, reply.Status);
        Assert.Equal("removed", reply.Message);
    }

    [Fact]
    public async Task Heartbeat_service_replays_pending_only_after_client_reports_active_session()
    {
        var directory = new InMemoryGameSessionRegistry();
        var reliablePush = new RecordingReliablePushRuntime();
        var service = new GameHeartbeatService(directory, reliablePush);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new object(), TestContext.Current.CancellationToken);

        var beforeClientReady = await service.HeartbeatAsync(
            "connection-a",
            new GameHeartbeatRequest(),
            TestContext.Current.CancellationToken);

        var afterClientReady = await service.HeartbeatAsync(
            "connection-a",
            new GameHeartbeatRequest
            {
                SessionId = session.SessionId
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(GameHeartbeatStatus.Ok, beforeClientReady.Status);
        Assert.Equal(GameHeartbeatStatus.Ok, afterClientReady.Status);
        Assert.Equal([session], reliablePush.Replayed);
    }

    [Fact]
    public async Task Heartbeat_service_reports_state_lost_for_client_session_mismatch()
    {
        var directory = new InMemoryGameSessionRegistry();
        var reliablePush = new RecordingReliablePushRuntime();
        var service = new GameHeartbeatService(directory, reliablePush);
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "connection-a", new object(), TestContext.Current.CancellationToken);

        var reply = await service.HeartbeatAsync(
            "connection-a",
            new GameHeartbeatRequest
            {
                SessionId = "stale-session"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(GameHeartbeatStatus.StateLost, reply.Status);
        Assert.Empty(reliablePush.Replayed);
    }

    private sealed class RecordingReliablePushRuntime : IReliablePushRuntime
    {
        public List<GameSessionKey> Replayed { get; } = [];

        public ValueTask<ClientNotificationStatus> PublishAsync(
            GameSessionKey session,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Failed);
        }

        public ValueTask ReplayPendingAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            Replayed.Add(session);
            return default;
        }

        public ValueTask<ReliablePushAckOutcome> AckAsync(
            GameSessionKey currentSession,
            GameSessionKey acknowledgedSession,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted());
        }
    }
}
