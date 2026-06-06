using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ULinkRpcNodeDirectoryTests
{
    [Fact]
    public async Task ClientCallsNodeDirectoryMethodsAndMapsReplies()
    {
        var client = new RecordingRpcClient();
        var directory = new ULinkRpcNodeDirectory(client);
        var now = DateTimeOffset.UtcNow;
        var registration = TestRegistration("local", "node-a", now);
        var record = TestRecord(registration, nodeEpoch: 7, updatedAt: now);

        client.Enqueue(new ULinkRpcNodeRegisterReply
        {
            Status = (int)NodeRegistrationStatus.Registered,
            Record = ULinkRpcNodeDirectoryRecordConverter.ToDto(record)
        });
        client.Enqueue(new ULinkRpcNodeHeartbeatReply
        {
            Status = (int)NodeHeartbeatStatus.Refreshed
        });
        client.Enqueue(new ULinkRpcNodeUpdateStateReply
        {
            Status = (int)NodeStateUpdateStatus.Updated
        });
        client.Enqueue(new ULinkRpcNodeResolveReply
        {
            Record = ULinkRpcNodeDirectoryRecordConverter.ToDto(record)
        });
        client.Enqueue(new ULinkRpcNodeQueryReply
        {
            Records = new List<ULinkRpcNodeRecordDto>
            {
                ULinkRpcNodeDirectoryRecordConverter.ToDto(record)
            }
        });
        client.Enqueue(new ULinkRpcNodeExpireReply
        {
            Expired = 1
        });

        var registered = await directory.RegisterAsync(registration, now, TestContext.Current.CancellationToken);
        var heartbeat = await directory.HeartbeatAsync("local", "node-a", record.NodeEpoch, now.AddSeconds(40), now, TestContext.Current.CancellationToken);
        var update = await directory.UpdateStateAsync("local", "node-a", record.NodeEpoch, NodeState.Draining, now, TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync("local", "node-a", now, TestContext.Current.CancellationToken);
        var queried = await directory.QueryAsync(new NodeDirectoryQuery("local", serviceKind: "gateway"), now, TestContext.Current.CancellationToken);
        var expired = await directory.ExpireAsync("local", now.AddMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal(NodeRegistrationStatus.Registered, registered.Status);
        Assert.NotNull(registered.Record);
        Assert.Equal(record.NodeEpoch, registered.Record!.NodeEpoch);
        Assert.Equal(NodeHeartbeatStatus.Refreshed, heartbeat);
        Assert.Equal(NodeStateUpdateStatus.Updated, update);
        Assert.NotNull(resolved);
        Assert.Equal(record.NodeEpoch, resolved!.NodeEpoch);
        Assert.Single(queried);
        Assert.Equal(record.NodeId, queried[0].NodeId);
        Assert.Equal(1, expired);
        Assert.Equal(new[]
        {
            ULinkRpcClusterProtocol.RegisterNodeMethodId,
            ULinkRpcClusterProtocol.HeartbeatNodeMethodId,
            ULinkRpcClusterProtocol.UpdateNodeStateMethodId,
            ULinkRpcClusterProtocol.ResolveNodeMethodId,
            ULinkRpcClusterProtocol.QueryNodesMethodId,
            ULinkRpcClusterProtocol.ExpireNodesMethodId
        }, client.MethodIds);
    }

    [Fact]
    public async Task ClientMapsUnknownNodeDirectoryStatusesConservatively()
    {
        var client = new RecordingRpcClient();
        var directory = new ULinkRpcNodeDirectory(client);
        var now = DateTimeOffset.UtcNow;

        client.Enqueue(new ULinkRpcNodeRegisterReply { Status = 99 });
        client.Enqueue(new ULinkRpcNodeHeartbeatReply { Status = 99 });
        client.Enqueue(new ULinkRpcNodeUpdateStateReply { Status = 99 });

        var registered = await directory.RegisterAsync(TestRegistration("local", "node-a", now), now, TestContext.Current.CancellationToken);
        var heartbeat = await directory.HeartbeatAsync("local", "node-a", 1, now.AddSeconds(30), now, TestContext.Current.CancellationToken);
        var update = await directory.UpdateStateAsync("local", "node-a", 1, NodeState.Ready, now, TestContext.Current.CancellationToken);

        Assert.Equal(NodeRegistrationStatus.InvalidRegistration, registered.Status);
        Assert.Null(registered.Record);
        Assert.Equal(NodeHeartbeatStatus.EpochMismatch, heartbeat);
        Assert.Equal(NodeStateUpdateStatus.EpochMismatch, update);
    }

    [Fact]
    public async Task ClientTreatsRegisteredReplyWithoutRecordAsInvalidRegistration()
    {
        var client = new RecordingRpcClient();
        var directory = new ULinkRpcNodeDirectory(client);
        var now = DateTimeOffset.UtcNow;

        client.Enqueue(new ULinkRpcNodeRegisterReply
        {
            Status = (int)NodeRegistrationStatus.Registered,
            Record = null
        });

        var registered = await directory.RegisterAsync(TestRegistration("local", "node-a", now), now, TestContext.Current.CancellationToken);

        Assert.Equal(NodeRegistrationStatus.InvalidRegistration, registered.Status);
        Assert.Null(registered.Record);
    }

    [Fact]
    public async Task ClientRejectsRecordReplyWithInvalidNodeState()
    {
        var client = new RecordingRpcClient();
        var directory = new ULinkRpcNodeDirectory(client);
        var now = DateTimeOffset.UtcNow;
        var record = ULinkRpcNodeDirectoryRecordConverter.ToDto(TestRecord(TestRegistration("local", "node-a", now), 1, now));
        record.State = 99;
        client.Enqueue(new ULinkRpcNodeResolveReply
        {
            Record = record
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            directory.ResolveAsync("local", "node-a", now, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void ConverterRejectsQueryWithInvalidNodeState()
    {
        var query = new ULinkRpcNodeDirectoryQueryDto
        {
            ClusterName = "local",
            State = 99
        };

        Assert.Throws<InvalidOperationException>(() =>
            ULinkRpcNodeDirectoryRecordConverter.ToNodeDirectoryQuery(query));
    }

    [Fact]
    public async Task BinderRegistersNodeHandlersAndUsesDirectorySemantics()
    {
        var registry = new RpcServiceRegistry();
        var directory = new InMemoryNodeDirectory();
        ULinkRpcNodeDirectoryBinder.Bind(registry, directory);
        var serializer = new JsonTestSerializer();
        await using var session = new RpcSession(new FakeTransport(), serializer);
        var now = DateTimeOffset.UtcNow;
        var registration = TestRegistration("local", "node-a", now);

        var register = await InvokeAsync<ULinkRpcNodeRegisterRequest, ULinkRpcNodeRegisterReply>(
            registry,
            session,
            ULinkRpcClusterProtocol.RegisterNodeMethodId,
            new ULinkRpcNodeRegisterRequest
            {
                Registration = ULinkRpcNodeDirectoryRecordConverter.ToDto(registration),
                Now = now
            });
        var heartbeat = await InvokeAsync<ULinkRpcNodeHeartbeatRequest, ULinkRpcNodeHeartbeatReply>(
            registry,
            session,
            ULinkRpcClusterProtocol.HeartbeatNodeMethodId,
            new ULinkRpcNodeHeartbeatRequest
            {
                ClusterName = "local",
                Node = "node-a",
                NodeEpoch = register.Record!.NodeEpoch,
                LeaseExpiresAt = now.AddSeconds(40),
                Now = now.AddSeconds(1)
            });
        var resolve = await InvokeAsync<ULinkRpcNodeResolveRequest, ULinkRpcNodeResolveReply>(
            registry,
            session,
            ULinkRpcClusterProtocol.ResolveNodeMethodId,
            new ULinkRpcNodeResolveRequest
            {
                ClusterName = "local",
                Node = "node-a",
                Now = now.AddSeconds(2)
            });

        Assert.Equal(NodeRegistrationStatus.Registered, (NodeRegistrationStatus)register.Status);
        Assert.NotNull(register.Record);
        Assert.Equal(NodeHeartbeatStatus.Refreshed, (NodeHeartbeatStatus)heartbeat.Status);
        Assert.NotNull(resolve.Record);
        Assert.Equal(register.Record.NodeEpoch, resolve.Record.NodeEpoch);
        Assert.Equal("tcp://127.0.0.1:21001", resolve.Record.Endpoints!["cluster"].Address);
        Assert.Equal("tcp", resolve.Record.Endpoints["cluster"].Metadata!["transport"]);
        Assert.Equal("gateway", resolve.Record.Services![0].Kind);
        Assert.Equal("gateway-a", resolve.Record.Services[0].Name);
        Assert.Equal("us-east", resolve.Record.Services[0].Metadata!["region"]);
    }

    [Fact]
    public async Task QueryReturnsServiceFilteredNodes()
    {
        var directory = new InMemoryNodeDirectory();
        var registry = new RpcServiceRegistry();
        ULinkRpcNodeDirectoryBinder.Bind(registry, directory);
        var serializer = new JsonTestSerializer();
        await using var session = new RpcSession(new FakeTransport(), serializer);
        var now = DateTimeOffset.UtcNow;

        await InvokeAsync<ULinkRpcNodeRegisterRequest, ULinkRpcNodeRegisterReply>(
            registry,
            session,
            ULinkRpcClusterProtocol.RegisterNodeMethodId,
            new ULinkRpcNodeRegisterRequest
            {
                Registration = ULinkRpcNodeDirectoryRecordConverter.ToDto(TestRegistration("local", "gateway-1", now, "gateway")),
                Now = now
            });
        await InvokeAsync<ULinkRpcNodeRegisterRequest, ULinkRpcNodeRegisterReply>(
            registry,
            session,
            ULinkRpcClusterProtocol.RegisterNodeMethodId,
            new ULinkRpcNodeRegisterRequest
            {
                Registration = ULinkRpcNodeDirectoryRecordConverter.ToDto(TestRegistration("local", "room-1", now, "room")),
                Now = now
            });

        var query = await InvokeAsync<ULinkRpcNodeQueryRequest, ULinkRpcNodeQueryReply>(
            registry,
            session,
            ULinkRpcClusterProtocol.QueryNodesMethodId,
            new ULinkRpcNodeQueryRequest
            {
                Query = ULinkRpcNodeDirectoryRecordConverter.ToDto(new NodeDirectoryQuery("local", serviceKind: "room")),
                Now = now
            });

        Assert.NotNull(query.Records);
        Assert.Single(query.Records);
        Assert.Equal("room-1", query.Records[0].Node);
    }

    [Fact]
    public async Task BinderRejectsInvalidInboundNodeState()
    {
        var registry = new RpcServiceRegistry();
        ULinkRpcNodeDirectoryBinder.Bind(registry, new InMemoryNodeDirectory());
        var serializer = new JsonTestSerializer();
        await using var session = new RpcSession(new FakeTransport(), serializer);
        var now = DateTimeOffset.UtcNow;
        var registration = ULinkRpcNodeDirectoryRecordConverter.ToDto(TestRegistration("local", "node-a", now));
        registration.State = 99;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync<ULinkRpcNodeRegisterRequest, ULinkRpcNodeRegisterReply>(
                registry,
                session,
                ULinkRpcClusterProtocol.RegisterNodeMethodId,
                new ULinkRpcNodeRegisterRequest
                {
                    Registration = registration,
                    Now = now
                }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync<ULinkRpcNodeUpdateStateRequest, ULinkRpcNodeUpdateStateReply>(
                registry,
                session,
                ULinkRpcClusterProtocol.UpdateNodeStateMethodId,
                new ULinkRpcNodeUpdateStateRequest
                {
                    ClusterName = "local",
                    Node = "node-a",
                    NodeEpoch = 1,
                    State = 99,
                    Now = now
                }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync<ULinkRpcNodeQueryRequest, ULinkRpcNodeQueryReply>(
                registry,
                session,
                ULinkRpcClusterProtocol.QueryNodesMethodId,
                new ULinkRpcNodeQueryRequest
                {
                    Query = new ULinkRpcNodeDirectoryQueryDto
                    {
                        ClusterName = "local",
                        State = 99
                    },
                    Now = now
                }));
    }

    private static async Task<TReply> InvokeAsync<TRequest, TReply>(
        RpcServiceRegistry registry,
        RpcSession session,
        int methodId,
        TRequest request)
    {
        Assert.True(registry.TryGetHandler(ULinkRpcClusterProtocol.ServiceId, methodId, out var handler));
        using var payload = session.Serializer.SerializeFrame(request);
        using var frame = await handler!(
            session,
            new RpcRequestFrame(1, ULinkRpcClusterProtocol.ServiceId, methodId, payload),
            TestContext.Current.CancellationToken);
        using var response = RpcEnvelopeCodec.DecodeResponse(frame);
        Assert.Equal(RpcStatus.Ok, response.Status);
        return session.Serializer.Deserialize<TReply>(response.Payload.Memory);
    }

    private static NodeRegistration TestRegistration(
        string clusterName,
        string nodeId,
        DateTimeOffset now,
        string serviceKind = "gateway")
    {
        return new NodeRegistration(
            clusterName,
            nodeId,
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint(
                    "tcp://127.0.0.1:21001",
                    new Dictionary<string, string>
                    {
                        ["transport"] = "tcp"
                    })
            },
            new[]
            {
                new NodeServiceDescriptor(
                    serviceKind,
                    serviceKind == "gateway" ? "gateway-a" : "room-a",
                    new Dictionary<string, string>
                    {
                        ["region"] = "us-east"
                    })
            },
            now.AddSeconds(30),
            NodeState.Ready,
            new Dictionary<string, string>
            {
                ["zone"] = "a"
            });
    }

    private static NodeRecord TestRecord(NodeRegistration registration, long nodeEpoch, DateTimeOffset updatedAt)
    {
        return new NodeRecord(
            registration.ClusterName,
            registration.NodeId,
            nodeEpoch,
            registration.Endpoints,
            registration.Services,
            registration.Labels,
            registration.State,
            registration.LeaseExpiresAt,
            updatedAt);
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        private readonly Queue<object> _replies = new();

        public List<int> MethodIds { get; } = new();

        public void Enqueue(object reply)
        {
            _replies.Enqueue(reply);
        }

        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            MethodIds.Add(method.MethodId);
            return ValueTask.FromResult((TResult)_replies.Dequeue());
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }

    private sealed class JsonTestSerializer : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value)
        {
            return TransportFrame.CopyOf(JsonSerializer.SerializeToUtf8Bytes(value));
        }

        public T Deserialize<T>(ReadOnlySpan<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload)!;
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return Deserialize<T>(payload.Span);
        }
    }

    private sealed class FakeTransport : ITransport
    {
        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            return default;
        }

        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }
}
