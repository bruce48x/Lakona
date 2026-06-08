using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterNodeMessengerTests
{
    [Fact]
    public async Task SendAsyncCallsClusterRpcMethodAndMapsReplyStatus()
    {
        var client = new RecordingRpcClient(new ClusterSendReply
        {
            Status = (int)ClusterSendStatus.Backpressure
        });
        var factory = new RecordingClientFactory(client);
        var messenger = new ClusterNodeMessenger(factory);
        var target = NewLocation();
        var message = NewMessage();

        var status = await messenger.SendAsync(target, message, TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Backpressure, status);
        Assert.Same(target, factory.Targets.Single());
        Assert.NotNull(client.Request);
        Assert.Equal(ClusterProtocol.ServiceId, client.ServiceId);
        Assert.Equal(ClusterProtocol.SendMethodId, client.MethodId);
        Assert.Equal("room/1", client.Request.Route);
        Assert.Equal("command", client.Request.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, client.Request.Payload);
        Assert.Equal("source", client.Request.SourceNode);
        Assert.Equal("corr-1", client.Request.CorrelationId);
    }

    [Fact]
    public async Task SendAsyncReturnsTimeoutWhenCallExceedsConfiguredTimeout()
    {
        var client = new HangingRpcClient();
        var messenger = new ClusterNodeMessenger(
            new RecordingClientFactory(client),
            new ClusterNodeMessengerOptions
            {
                SendTimeout = TimeSpan.FromMilliseconds(1)
            });

        var status = await messenger.SendAsync(
            NewLocation(),
            NewMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Timeout, status);
    }

    [Fact]
    public async Task SendAsyncUsesExceptionMapperWhenProvided()
    {
        var client = new ThrowingRpcClient(new InvalidOperationException("remote handler missing"));
        var messenger = new ClusterNodeMessenger(
            new RecordingClientFactory(client),
            new ClusterNodeMessengerOptions
            {
                ExceptionMapper = _ => ClusterSendStatus.HandlerUnavailable
            });

        var status = await messenger.SendAsync(
            NewLocation(),
            NewMessage(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.HandlerUnavailable, status);
    }

    private static RouteLocation NewLocation()
    {
        return new RouteLocation(
            "room/1",
            "node-b",
            new NodeEndpoint("lakona-rpc://node-b:20010"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            nodeEpoch: 2,
            generation: 3);
    }

    private static ClusterMessage NewMessage()
    {
        return new ClusterMessage(
            "room/1",
            "command",
            new byte[] { 1, 2, 3 },
            DateTimeOffset.UtcNow.AddMinutes(1),
            "source",
            correlationId: "corr-1",
            traceId: "trace-1",
            orderedBy: "room/1");
    }

    private sealed class RecordingClientFactory : IClusterClientFactory
    {
        private readonly IRpcClient _client;

        public RecordingClientFactory(IRpcClient client)
        {
            _client = client;
        }

        public List<RouteLocation> Targets { get; } = new();

        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Targets.Add(target);
            return ValueTask.FromResult(_client);
        }
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        private readonly ClusterSendReply _reply;

        public RecordingRpcClient(ClusterSendReply reply)
        {
            _reply = reply;
        }

        public int ServiceId { get; private set; }

        public int MethodId { get; private set; }

        public ClusterSendRequest? Request { get; private set; }

        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ServiceId = method.ServiceId;
            MethodId = method.MethodId;
            Request = Assert.IsType<ClusterSendRequest>(arg);
            return ValueTask.FromResult((TResult)(object)_reply);
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }

    private sealed class HangingRpcClient : IRpcClient
    {
        public async ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }

    private sealed class ThrowingRpcClient : IRpcClient
    {
        private readonly Exception _exception;

        public ThrowingRpcClient(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            throw _exception;
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }
}
