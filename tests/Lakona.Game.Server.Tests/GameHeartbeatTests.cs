using Lakona.Game.Abstractions.Sessions;
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
}
