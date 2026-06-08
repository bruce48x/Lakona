using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class RouteDirectoryClientTests
{
    [Fact]
    public async Task ClientCallsRouteDirectoryMethodsAndMapsReplies()
    {
        var client = new RecordingRpcClient();
        var directory = new RouteDirectoryClient(client);
        var now = DateTimeOffset.UtcNow;
        var location = NewLocation(now.AddMinutes(1));

        client.Enqueue(new RouteRegisterReply
        {
            Status = (int)RouteRegistrationStatus.Registered
        });
        client.Enqueue(new RouteResolveReply
        {
            Location = RouteLocationConverter.ToDto(location)
        });
        client.Enqueue(new RouteRefreshLeaseReply
        {
            Status = (int)RouteLeaseRefreshStatus.Refreshed
        });
        client.Enqueue(new RouteExpireReply
        {
            Removed = 1
        });
        client.Enqueue(new RouteClearReply
        {
            Removed = 2
        });
        client.Enqueue(new RouteClearReply
        {
            Removed = 3
        });

        var register = await directory.RegisterAsync(location, TestContext.Current.CancellationToken);
        var resolved = await directory.ResolveAsync(location.Route, now, TestContext.Current.CancellationToken);
        var refresh = await directory.RefreshLeaseAsync(location, now.AddMinutes(2), now, TestContext.Current.CancellationToken);
        var expired = await directory.ExpireAsync(now.AddMinutes(3), TestContext.Current.CancellationToken);
        var clearedByNode = await directory.ClearByNodeAsync(location.Node, TestContext.Current.CancellationToken);
        var clearedByEpoch = await directory.ClearByNodeEpochAsync(location.Node, location.NodeEpoch, TestContext.Current.CancellationToken);

        Assert.Equal(RouteRegistrationStatus.Registered, register);
        Assert.Equal(RouteLeaseRefreshStatus.Refreshed, refresh);
        Assert.Equal(1, expired);
        Assert.Equal(2, clearedByNode);
        Assert.Equal(3, clearedByEpoch);
        Assert.NotNull(resolved);
        Assert.Equal(location.Route, resolved.Route);
        Assert.Equal(location.Node, resolved.Node);
        Assert.Equal(location.Endpoint.Address, resolved.Endpoint.Address);
        Assert.Equal(location.NodeEpoch, resolved.NodeEpoch);
        Assert.Equal(location.Generation, resolved.Generation);
        Assert.Equal(new[]
        {
            ClusterProtocol.RegisterRouteMethodId,
            ClusterProtocol.ResolveRouteMethodId,
            ClusterProtocol.RefreshRouteLeaseMethodId,
            ClusterProtocol.ExpireRoutesMethodId,
            ClusterProtocol.ClearRoutesByNodeMethodId,
            ClusterProtocol.ClearRoutesByNodeEpochMethodId
        }, client.MethodIds);
    }

    [Fact]
    public async Task BinderRegistersRouteHandlersAndUsesDirectorySemantics()
    {
        var registry = new RpcServiceRegistry();
        var directory = new InMemoryRouteDirectory();
        RouteDirectoryBinder.Bind(registry, directory);
        var serializer = new JsonTestSerializer();
        await using var session = new RpcSession(new FakeTransport(), serializer);
        var now = DateTimeOffset.UtcNow;
        var location = NewLocation(now.AddMinutes(1));

        var register = await InvokeAsync<RouteRegisterRequest, RouteRegisterReply>(
            registry,
            session,
            ClusterProtocol.RegisterRouteMethodId,
            new RouteRegisterRequest
            {
                Location = RouteLocationConverter.ToDto(location)
            });
        var resolve = await InvokeAsync<RouteResolveRequest, RouteResolveReply>(
            registry,
            session,
            ClusterProtocol.ResolveRouteMethodId,
            new RouteResolveRequest
            {
                Route = location.Route.Value,
                Now = now
            });
        var refresh = await InvokeAsync<RouteRefreshLeaseRequest, RouteRefreshLeaseReply>(
            registry,
            session,
            ClusterProtocol.RefreshRouteLeaseMethodId,
            new RouteRefreshLeaseRequest
            {
                ExpectedLocation = RouteLocationConverter.ToDto(location),
                ExpiresAt = now.AddMinutes(2),
                Now = now
            });
        var clear = await InvokeAsync<RouteClearByNodeEpochRequest, RouteClearReply>(
            registry,
            session,
            ClusterProtocol.ClearRoutesByNodeEpochMethodId,
            new RouteClearByNodeEpochRequest
            {
                Node = location.Node.Value,
                NodeEpoch = location.NodeEpoch
            });

        Assert.Equal(RouteRegistrationStatus.Registered, (RouteRegistrationStatus)register.Status);
        Assert.NotNull(resolve.Location);
        Assert.Equal(location.Route.Value, resolve.Location.Route);
        Assert.Equal(RouteLeaseRefreshStatus.Refreshed, (RouteLeaseRefreshStatus)refresh.Status);
        Assert.Equal(1, clear.Removed);
    }

    private static async Task<TReply> InvokeAsync<TRequest, TReply>(
        RpcServiceRegistry registry,
        RpcSession session,
        int methodId,
        TRequest request)
    {
        Assert.True(registry.TryGetHandler(ClusterProtocol.ServiceId, methodId, out var handler));
        using var payload = session.Serializer.SerializeFrame(request);
        using var frame = await handler!(
            session,
            new RpcRequestFrame(1, ClusterProtocol.ServiceId, methodId, payload),
            TestContext.Current.CancellationToken);
        using var response = RpcEnvelopeCodec.DecodeResponse(frame);
        Assert.Equal(RpcStatus.Ok, response.Status);
        return session.Serializer.Deserialize<TReply>(response.Payload.Memory);
    }

    private static RouteLocation NewLocation(DateTimeOffset expiresAt)
    {
        return new RouteLocation(
            "room/1",
            "node-b",
            new NodeEndpoint(
                "tcp://127.0.0.1:20010",
                new Dictionary<string, string>
                {
                    ["transport"] = "tcp"
                }),
            expiresAt,
            nodeEpoch: 2,
            generation: 3,
            metadata: new Dictionary<string, string>
            {
                ["role"] = "room"
            });
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
