using Server.App.State.Contracts;
using Server.App.State.Contracts.Sessions;
using Lakona.Game.Cluster.Rpc.MemoryPack;
using Server.Hotfix.Features;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarFeatureCommandSerializationTests
{
    [Fact]
    public void BattleRuntimeFeatureCommandDtosRoundTripWithConfiguredMemoryPackSerializer()
    {
        var serializer = ClusterRpcMemoryPack.CreateSerializer();
        var request = new BattleRuntimeRoomAllocationRequest
        {
            RoomId = "room-1",
            MaxPlayers = 10,
            Players =
            [
                new PlayerRoomAssignment
                {
                    UserId = "user-1",
                    RoomId = "room-1",
                    MatchId = "match-1",
                    SeatIndex = 0,
                    SessionToken = "session-1",
                    ConnectionId = "connection-1",
                    AssignedAtUtc = new DateTime(2026, 6, 29, 8, 0, 1, DateTimeKind.Utc),
                    RuntimeGateway = new GatewayEndpointDescriptor
                    {
                        InstanceId = "runtime-1",
                        Transport = "kcp",
                        Host = "127.0.0.1",
                        Port = 7001,
                        Path = ""
                    }
                }
            ]
        };

        using var frame = serializer.SerializeFrame(request);
        var decoded = serializer.Deserialize<BattleRuntimeRoomAllocationRequest>(frame.Memory);

        Assert.Equal("room-1", decoded.RoomId);
        Assert.Equal(10, decoded.MaxPlayers);
        Assert.Single(decoded.Players);
        Assert.Equal("match-1", decoded.Players[0].MatchId);
        Assert.Equal("runtime-1", decoded.Players[0].RuntimeGateway.InstanceId);
    }

    [Fact]
    public void BattleRuntimeFeatureCommandReplyRoundTripsWithConfiguredMemoryPackSerializer()
    {
        var serializer = ClusterRpcMemoryPack.CreateSerializer();
        var reply = new BattleRuntimeRoomAllocationReply
        {
            Succeeded = true,
            RoomId = "room-1",
            Message = "Room allocated."
        };

        using var frame = serializer.SerializeFrame(reply);
        var decoded = serializer.Deserialize<BattleRuntimeRoomAllocationReply>(frame.Memory);

        Assert.True(decoded.Succeeded);
        Assert.Equal("room-1", decoded.RoomId);
        Assert.Equal("Room allocated.", decoded.Message);
    }
}
