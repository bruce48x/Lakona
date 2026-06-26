using Lakona.Game.Cluster.Rpc.MemoryPack;
using MemoryPack;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed partial class LegacyMemoryPackWireCompatibilityTests
{
    [Fact]
    public void ClusterSendRequest_matches_legacy_memorypack_wire_bytes()
    {
        var expiresAt = new DateTimeOffset(2026, 6, 24, 1, 2, 3, TimeSpan.Zero);
        var legacy = new LegacyClusterSendRequest
        {
            Route = "actor:room/1",
            Kind = "join",
            Payload = [1, 2, 3],
            ExpiresAt = expiresAt,
            SourceNode = "gateway-1",
            CorrelationId = "corr-1",
            TraceId = "trace-1",
            OrderedBy = "room/1"
        };
        var current = new ClusterSendRequest
        {
            Route = legacy.Route,
            Kind = legacy.Kind,
            Payload = legacy.Payload,
            ExpiresAt = legacy.ExpiresAt,
            SourceNode = legacy.SourceNode,
            CorrelationId = legacy.CorrelationId,
            TraceId = legacy.TraceId,
            OrderedBy = legacy.OrderedBy
        };

        AssertWireCompatible(legacy, current, decoded =>
        {
            Assert.Equal("actor:room/1", decoded.Route);
            Assert.Equal("join", decoded.Kind);
            Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Payload);
            Assert.Equal(expiresAt, decoded.ExpiresAt);
            Assert.Equal("gateway-1", decoded.SourceNode);
            Assert.Equal("corr-1", decoded.CorrelationId);
            Assert.Equal("trace-1", decoded.TraceId);
            Assert.Equal("room/1", decoded.OrderedBy);
        });
    }

    [Fact]
    public void FeatureSendRequest_matches_legacy_memorypack_wire_bytes()
    {
        var expiresAt = new DateTimeOffset(2026, 6, 24, 2, 3, 4, TimeSpan.Zero);
        var legacy = new LegacyFeatureSendRequest
        {
            Feature = "matchmaking",
            Kind = "enqueue",
            Payload = [4, 5, 6],
            ExpiresAt = expiresAt,
            SourceNode = "gateway-1",
            CorrelationId = "corr-2"
        };
        var current = new FeatureSendRequest
        {
            Feature = legacy.Feature,
            Kind = legacy.Kind,
            Payload = legacy.Payload,
            ExpiresAt = legacy.ExpiresAt,
            SourceNode = legacy.SourceNode,
            CorrelationId = legacy.CorrelationId
        };

        AssertWireCompatible(legacy, current, decoded =>
        {
            Assert.Equal("matchmaking", decoded.Feature);
            Assert.Equal("enqueue", decoded.Kind);
            Assert.Equal(new byte[] { 4, 5, 6 }, decoded.Payload);
            Assert.Equal(expiresAt, decoded.ExpiresAt);
            Assert.Equal("gateway-1", decoded.SourceNode);
            Assert.Equal("corr-2", decoded.CorrelationId);
        });
    }

    [Fact]
    public void RouteRegisterRequest_matches_legacy_memorypack_wire_bytes()
    {
        var expiresAt = new DateTimeOffset(2026, 6, 24, 3, 4, 5, TimeSpan.Zero);
        var legacy = new LegacyRouteRegisterRequest
        {
            Location = new LegacyRouteLocationDto
            {
                Route = "actor:room/1",
                Node = "room-node-1",
                EndpointAddress = "tcp://127.0.0.1:21001",
                EndpointMetadata = new Dictionary<string, string> { ["transport"] = "tcp" },
                ExpiresAt = expiresAt,
                NodeEpoch = 7,
                Generation = 8,
                Metadata = new Dictionary<string, string> { ["role"] = "room" }
            }
        };
        var current = new RouteRegisterRequest
        {
            Location = new RouteLocationDto
            {
                Route = legacy.Location.Route,
                Node = legacy.Location.Node,
                EndpointAddress = legacy.Location.EndpointAddress,
                EndpointMetadata = legacy.Location.EndpointMetadata,
                ExpiresAt = legacy.Location.ExpiresAt,
                NodeEpoch = legacy.Location.NodeEpoch,
                Generation = legacy.Location.Generation,
                Metadata = legacy.Location.Metadata
            }
        };

        AssertWireCompatible(legacy, current, decoded =>
        {
            Assert.NotNull(decoded.Location);
            Assert.Equal("actor:room/1", decoded.Location.Route);
            Assert.Equal("room-node-1", decoded.Location.Node);
            Assert.Equal("tcp://127.0.0.1:21001", decoded.Location.EndpointAddress);
            Assert.Equal("tcp", decoded.Location.EndpointMetadata!["transport"]);
            Assert.Equal(expiresAt, decoded.Location.ExpiresAt);
            Assert.Equal(7, decoded.Location.NodeEpoch);
            Assert.Equal(8, decoded.Location.Generation);
            Assert.Equal("room", decoded.Location.Metadata!["role"]);
        });
    }

    [Fact]
    public void NodeRegisterRequest_matches_legacy_memorypack_wire_bytes()
    {
        var leaseExpiresAt = new DateTimeOffset(2026, 6, 24, 4, 5, 6, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 6, 24, 4, 0, 0, TimeSpan.Zero);
        var legacy = new LegacyNodeRegisterRequest
        {
            Registration = new LegacyNodeRegistrationDto
            {
                ClusterName = "local",
                Node = "gateway-1",
                Endpoints = new Dictionary<string, LegacyNodeEndpointDto>
                {
                    ["cluster"] = new LegacyNodeEndpointDto
                    {
                        Address = "tcp://127.0.0.1:22001",
                        Metadata = new Dictionary<string, string> { ["transport"] = "tcp" }
                    }
                },
                Features =
                [
                    new LegacyNodeFeatureDto
                    {
                        Name = "gateway",
                        Metadata = new Dictionary<string, string> { ["region"] = "us-east" }
                    }
                ],
                Labels = new Dictionary<string, string> { ["zone"] = "a" },
                State = 1,
                LeaseExpiresAt = leaseExpiresAt
            },
            Now = now
        };
        var current = new NodeRegisterRequest
        {
            Registration = new NodeRegistrationDto
            {
                ClusterName = legacy.Registration.ClusterName,
                Node = legacy.Registration.Node,
                Endpoints = legacy.Registration.Endpoints.ToDictionary(
                    entry => entry.Key,
                    entry => new NodeEndpointDto
                    {
                        Address = entry.Value.Address,
                        Metadata = entry.Value.Metadata
                    }),
                Features = legacy.Registration.Features
                    .Select(feature => new NodeFeatureDto
                    {
                        Name = feature.Name,
                        Metadata = feature.Metadata
                    })
                    .ToList(),
                Labels = legacy.Registration.Labels,
                State = legacy.Registration.State,
                LeaseExpiresAt = legacy.Registration.LeaseExpiresAt
            },
            Now = legacy.Now
        };

        AssertWireCompatible(legacy, current, decoded =>
        {
            Assert.NotNull(decoded.Registration);
            Assert.Equal("local", decoded.Registration.ClusterName);
            Assert.Equal("gateway-1", decoded.Registration.Node);
            Assert.Equal("tcp://127.0.0.1:22001", decoded.Registration.Endpoints!["cluster"].Address);
            Assert.Equal("tcp", decoded.Registration.Endpoints["cluster"].Metadata!["transport"]);
            Assert.Equal("gateway", Assert.Single(decoded.Registration.Features!).Name);
            Assert.Equal("a", decoded.Registration.Labels!["zone"]);
            Assert.Equal(1, decoded.Registration.State);
            Assert.Equal(leaseExpiresAt, decoded.Registration.LeaseExpiresAt);
            Assert.Equal(now, decoded.Now);
        });
    }

    [Fact]
    public void ClientNotificationDispatchRequest_matches_legacy_memorypack_wire_bytes()
    {
        var legacy = new LegacyClientNotificationDispatchRequest
        {
            Command = new LegacyClientNotificationCommand
            {
                OwnerKey = "player-1",
                SessionId = "session-1",
                Generation = 2,
                CallbackContractType = "Game.ILoginCallback",
                MethodName = "OnMatchedAsync",
                Arguments =
                [
                    new LegacyClientNotificationArgument
                    {
                        TypeName = "System.String",
                        Payload = [7, 8, 9]
                    }
                ]
            }
        };
        var current = new ClientNotificationDispatchRequest
        {
            Command = new ClientNotificationCommand
            {
                OwnerKey = legacy.Command.OwnerKey,
                SessionId = legacy.Command.SessionId,
                Generation = legacy.Command.Generation,
                CallbackContractType = legacy.Command.CallbackContractType,
                MethodName = legacy.Command.MethodName,
                Arguments = legacy.Command.Arguments
                    .Select(argument => new ClientNotificationArgument
                    {
                        TypeName = argument.TypeName,
                        Payload = argument.Payload
                    })
                    .ToList()
            }
        };

        AssertWireCompatible(legacy, current, decoded =>
        {
            Assert.NotNull(decoded.Command);
            Assert.Equal("player-1", decoded.Command.OwnerKey);
            Assert.Equal("session-1", decoded.Command.SessionId);
            Assert.Equal(2, decoded.Command.Generation);
            Assert.Equal("Game.ILoginCallback", decoded.Command.CallbackContractType);
            Assert.Equal("OnMatchedAsync", decoded.Command.MethodName);
            var argument = Assert.Single(decoded.Command.Arguments);
            Assert.Equal("System.String", argument.TypeName);
            Assert.Equal(new byte[] { 7, 8, 9 }, argument.Payload);
        });
    }

    private static void AssertWireCompatible<TLegacy, TCurrent>(
        TLegacy legacy,
        TCurrent current,
        Action<TCurrent> assertDecoded)
    {
        var legacyBytes = MemoryPackSerializer.Serialize(legacy);
        var serializer = ClusterRpcMemoryPack.CreateSerializer();
        using var currentFrame = serializer.SerializeFrame(current);
        var currentBytes = currentFrame.Memory.ToArray();

        if (!WireBytesMatch(legacyBytes, currentBytes))
        {
            var mismatch = Enumerable
                .Range(0, Math.Min(legacyBytes.Length, currentBytes.Length))
                .First(index => legacyBytes[index] != currentBytes[index]);
            var start = Math.Max(0, mismatch - 16);
            var expectedWindow = Convert.ToHexString(legacyBytes.Skip(start).Take(40).ToArray());
            var actualWindow = Convert.ToHexString(currentBytes.Skip(start).Take(40).ToArray());
            Assert.Fail($"MemoryPack bytes differ at {mismatch}. Expected[{start}..]={expectedWindow}; Actual[{start}..]={actualWindow}");
        }

        var decoded = serializer.Deserialize<TCurrent>(legacyBytes);
        assertDecoded(decoded);
    }

    private static bool WireBytesMatch(byte[] expected, byte[] actual)
    {
        if (expected.SequenceEqual(actual))
        {
            return true;
        }

        if (expected.Length != actual.Length)
        {
            return false;
        }

        var mismatch = Enumerable.Range(0, expected.Length).First(index => expected[index] != actual[index]);
        var normalizedExpected = expected.ToArray();
        var normalizedActual = actual.ToArray();

        // MemoryPack's generated unmanaged DateTimeOffset bytes can include
        // runtime-dependent padding in nested version-tolerant DTOs. The
        // decoded assertions below still verify the semantic timestamp.
        Array.Clear(normalizedExpected, mismatch, Math.Min(4, normalizedExpected.Length - mismatch));
        Array.Clear(normalizedActual, mismatch, Math.Min(4, normalizedActual.Length - mismatch));
        return normalizedExpected.SequenceEqual(normalizedActual);
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyClusterSendRequest
    {
        [MemoryPackOrder(0)]
        public string Route { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Kind { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        [MemoryPackOrder(3)]
        public DateTimeOffset ExpiresAt { get; set; }

        [MemoryPackOrder(4)]
        public string SourceNode { get; set; } = string.Empty;

        [MemoryPackOrder(5)]
        public string? CorrelationId { get; set; }

        [MemoryPackOrder(6)]
        public string? TraceId { get; set; }

        [MemoryPackOrder(7)]
        public string? OrderedBy { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyFeatureSendRequest
    {
        [MemoryPackOrder(0)]
        public string Feature { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Kind { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        [MemoryPackOrder(3)]
        public DateTimeOffset ExpiresAt { get; set; }

        [MemoryPackOrder(4)]
        public string SourceNode { get; set; } = string.Empty;

        [MemoryPackOrder(5)]
        public string CorrelationId { get; set; } = string.Empty;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyRouteRegisterRequest
    {
        [MemoryPackOrder(0)]
        public LegacyRouteLocationDto? Location { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyRouteLocationDto
    {
        [MemoryPackOrder(0)]
        public string Route { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public string EndpointAddress { get; set; } = string.Empty;

        [MemoryPackOrder(3)]
        public Dictionary<string, string>? EndpointMetadata { get; set; }

        [MemoryPackOrder(4)]
        public DateTimeOffset ExpiresAt { get; set; }

        [MemoryPackOrder(5)]
        public long NodeEpoch { get; set; }

        [MemoryPackOrder(6)]
        public long Generation { get; set; }

        [MemoryPackOrder(7)]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyNodeRegisterRequest
    {
        [MemoryPackOrder(0)]
        public LegacyNodeRegistrationDto? Registration { get; set; }

        [MemoryPackOrder(1)]
        public DateTimeOffset Now { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyNodeRegistrationDto
    {
        [MemoryPackOrder(0)]
        public string ClusterName { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string Node { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        public Dictionary<string, LegacyNodeEndpointDto>? Endpoints { get; set; }

        [MemoryPackOrder(3)]
        public List<LegacyNodeFeatureDto>? Features { get; set; }

        [MemoryPackOrder(4)]
        public Dictionary<string, string>? Labels { get; set; }

        [MemoryPackOrder(5)]
        public int State { get; set; }

        [MemoryPackOrder(6)]
        public DateTimeOffset LeaseExpiresAt { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyNodeEndpointDto
    {
        [MemoryPackOrder(0)]
        public string Address { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyNodeFeatureDto
    {
        [MemoryPackOrder(0)]
        public string Name { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyClientNotificationDispatchRequest
    {
        [MemoryPackOrder(0)]
        public LegacyClientNotificationCommand? Command { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyClientNotificationCommand
    {
        [MemoryPackOrder(0)]
        public string OwnerKey { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SessionId { get; set; } = "";

        [MemoryPackOrder(2)]
        public long Generation { get; set; }

        [MemoryPackOrder(3)]
        public string CallbackContractType { get; set; } = "";

        [MemoryPackOrder(4)]
        public string MethodName { get; set; } = "";

        [MemoryPackOrder(5)]
        public IReadOnlyList<LegacyClientNotificationArgument> Arguments { get; set; } = [];
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    private sealed partial class LegacyClientNotificationArgument
    {
        [MemoryPackOrder(0)]
        public string TypeName { get; set; } = "";

        [MemoryPackOrder(1)]
        public byte[] Payload { get; set; } = [];
    }
}
