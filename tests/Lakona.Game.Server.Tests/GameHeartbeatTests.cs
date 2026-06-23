using Lakona.Game.Abstractions.Sessions;
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
}
