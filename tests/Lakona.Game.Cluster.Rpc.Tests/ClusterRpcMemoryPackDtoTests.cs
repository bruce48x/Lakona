using Lakona.Rpc.Serializer.MemoryPack;
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
            OrderedBy = "room/1"
        };

        var roundtripped = Roundtrip(request);

        Assert.Equal("actor:room/1", roundtripped.Route);
        Assert.Equal(new byte[] { 1, 2, 3 }, roundtripped.Payload);
        Assert.Equal("gateway-1", roundtripped.SourceNode);
    }

    [Fact]
    public void RoundtripsFeatureSendRequest()
    {
        var request = new FeatureSendRequest
        {
            Feature = "matchmaking",
            Kind = "enqueue",
            Payload = new byte[] { 4, 5, 6 },
            ExpiresAt = new DateTimeOffset(2026, 6, 24, 2, 3, 4, TimeSpan.Zero),
            SourceNode = "gateway-1",
            CorrelationId = "corr-2"
        };

        var roundtripped = Roundtrip(request);

        Assert.Equal("matchmaking", roundtripped.Feature);
        Assert.Equal("enqueue", roundtripped.Kind);
        Assert.Equal(new byte[] { 4, 5, 6 }, roundtripped.Payload);
        Assert.Equal("corr-2", roundtripped.CorrelationId);
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
    public void RoundtripsNodeRegisterRequest()
    {
        var request = new NodeRegisterRequest
        {
            Registration = new NodeRegistrationDto
            {
                ClusterName = "local",
                Node = "gateway-1",
                Endpoints = new Dictionary<string, NodeEndpointDto>
                {
                    ["cluster"] = new NodeEndpointDto
                    {
                        Address = "tcp://127.0.0.1:22001",
                        Metadata = new Dictionary<string, string>
                        {
                            ["transport"] = "tcp"
                        }
                    }
                },
                Features = new List<NodeFeatureDto>
                {
                    new NodeFeatureDto
                    {
                        Name = "gateway",
                        Metadata = new Dictionary<string, string>
                        {
                            ["region"] = "us-east"
                        }
                    }
                },
                Labels = new Dictionary<string, string>
                {
                    ["zone"] = "a"
                },
                State = 1,
                LeaseExpiresAt = new DateTimeOffset(2026, 6, 24, 4, 5, 6, TimeSpan.Zero)
            },
            Now = new DateTimeOffset(2026, 6, 24, 4, 0, 0, TimeSpan.Zero)
        };

        var roundtripped = Roundtrip(request);

        Assert.NotNull(roundtripped.Registration);
        Assert.Equal("local", roundtripped.Registration.ClusterName);
        Assert.Equal("gateway-1", roundtripped.Registration.Node);
        Assert.Equal("tcp://127.0.0.1:22001", roundtripped.Registration.Endpoints!["cluster"].Address);
        Assert.Equal("tcp", roundtripped.Registration.Endpoints["cluster"].Metadata!["transport"]);
        Assert.Equal("gateway", roundtripped.Registration.Features![0].Name);
        Assert.Equal("us-east", roundtripped.Registration.Features[0].Metadata!["region"]);
        Assert.Equal("a", roundtripped.Registration.Labels!["zone"]);
    }

    private static T Roundtrip<T>(T value)
    {
        var serializer = new MemoryPackRpcSerializer();
        using var frame = serializer.SerializeFrame(value);
        return serializer.Deserialize<T>(frame.Memory);
    }
}
