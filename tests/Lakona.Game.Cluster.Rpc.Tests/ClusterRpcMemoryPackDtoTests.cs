using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcMemoryPackDtoTests
{
    [Fact]
    public void RoundtripsClusterSendRequest()
    {
        var request = new ClusterSendRequest
        {
            Route = "actor:room/1",
            Kind = "join",
            Payload = new byte[] { 1, 2, 3 },
            ExpiresAt = new DateTimeOffset(2026, 6, 24, 1, 2, 3, TimeSpan.Zero),
            SourceNode = "gateway-1",
            CorrelationId = "corr-1",
            TraceId = "trace-1",
            OrderedBy = "room/1",
            Metadata = new Dictionary<string, string>
            {
                ["lakona-game.actor-api.method-id"] = "13234687008277710378"
            }
        };

        var roundtripped = Roundtrip(request);

        Assert.Equal("actor:room/1", roundtripped.Route);
        Assert.Equal(new byte[] { 1, 2, 3 }, roundtripped.Payload);
        Assert.Equal("gateway-1", roundtripped.SourceNode);
        Assert.NotNull(roundtripped.Metadata);
        Assert.Equal("13234687008277710378", roundtripped.Metadata["lakona-game.actor-api.method-id"]);
    }

    [Fact]
    public void RoundtripsRouteRegisterRequest()
    {
        var request = new RouteRegisterRequest
        {
            Location = new RouteLocationDto
            {
                Route = "actor:room/1",
                Node = "room-node-1",
                EndpointAddress = "tcp://127.0.0.1:21001",
                EndpointMetadata = new Dictionary<string, string>
                {
                    ["transport"] = "tcp"
                },
                ExpiresAt = new DateTimeOffset(2026, 6, 24, 3, 4, 5, TimeSpan.Zero),
                NodeEpoch = 7,
                Generation = 8,
                Metadata = new Dictionary<string, string>
                {
                    ["role"] = "room"
                }
            }
        };

        var roundtripped = Roundtrip(request);

        Assert.NotNull(roundtripped.Location);
        Assert.Equal("actor:room/1", roundtripped.Location.Route);
        Assert.Equal("room-node-1", roundtripped.Location.Node);
        Assert.Equal("tcp://127.0.0.1:21001", roundtripped.Location.EndpointAddress);
        Assert.Equal("tcp", roundtripped.Location.EndpointMetadata!["transport"]);
        Assert.Equal(7, roundtripped.Location.NodeEpoch);
        Assert.Equal("room", roundtripped.Location.Metadata!["role"]);
    }

    [Fact]
    public void RoundtripsRouteRefreshLeaseRequest()
    {
        var request = new RouteRefreshLeaseRequest
        {
            ExpectedLocation = new RouteLocationDto
            {
                Route = "actor:room/2",
                Node = "room-node-2",
                EndpointAddress = "tcp://127.0.0.1:21002",
                EndpointMetadata = new Dictionary<string, string>
                {
                    ["transport"] = "tcp"
                },
                ExpiresAt = new DateTimeOffset(2026, 6, 24, 6, 7, 8, TimeSpan.Zero),
                NodeEpoch = 10,
                Generation = 11,
                Metadata = new Dictionary<string, string>
                {
                    ["role"] = "room"
                }
            },
            ExpiresAt = new DateTimeOffset(2026, 6, 24, 7, 8, 9, TimeSpan.Zero),
            Now = new DateTimeOffset(2026, 6, 24, 6, 8, 9, TimeSpan.Zero)
        };

        var roundtripped = Roundtrip(request);

        Assert.NotNull(roundtripped.ExpectedLocation);
        Assert.Equal("actor:room/2", roundtripped.ExpectedLocation.Route);
        Assert.Equal("room-node-2", roundtripped.ExpectedLocation.Node);
        Assert.Equal(10, roundtripped.ExpectedLocation.NodeEpoch);
        Assert.Equal(11, roundtripped.ExpectedLocation.Generation);
        Assert.Equal(new DateTimeOffset(2026, 6, 24, 7, 8, 9, TimeSpan.Zero), roundtripped.ExpiresAt);
        Assert.Equal(new DateTimeOffset(2026, 6, 24, 6, 8, 9, TimeSpan.Zero), roundtripped.Now);
    }

    [Fact]
    public void RoundtripsClientNotificationDispatchRequest()
    {
        var request = new ClientNotificationDispatchRequest
        {
            Command = new ClientNotificationCommand
            {
                OwnerKey = "player-1",
                SessionId = "session-1",
                CallbackContractType = "Game.ILoginCallback",
                MethodName = "OnMatchedAsync",
                ServiceId = 17,
                MethodId = 23,
                Payload = [4, 5, 6],
                Metadata = new ClientNotificationMetadata
                {
                    Type = "lakona.game.reliable-push",
                    Payload = new byte[] { 1, 2, 3 }
                }
            }
        };

        var roundtripped = Roundtrip(request);

        Assert.NotNull(roundtripped.Command);
        Assert.Equal("player-1", roundtripped.Command.OwnerKey);
        Assert.Equal("session-1", roundtripped.Command.SessionId);
        Assert.Equal("Game.ILoginCallback", roundtripped.Command.CallbackContractType);
        Assert.Equal("OnMatchedAsync", roundtripped.Command.MethodName);
        Assert.Equal(17, roundtripped.Command.ServiceId);
        Assert.Equal(23, roundtripped.Command.MethodId);
        Assert.Equal(new byte[] { 4, 5, 6 }, roundtripped.Command.Payload);
        Assert.NotNull(roundtripped.Command.Metadata);
        Assert.Equal("lakona.game.reliable-push", roundtripped.Command.Metadata.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, roundtripped.Command.Metadata.Payload.ToArray());
    }

    private static T Roundtrip<T>(T value)
    {
        var serializer = new MemoryPackRpcSerializer();
        using var frame = serializer.SerializeFrame(value);
        return serializer.Deserialize<T>(frame.Memory);
    }
}
