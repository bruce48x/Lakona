using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Tcp;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class TcpULinkRpcClusterTransportFactoryTests
{
    [Fact]
    public async Task MessengerCanSendClusterMessageThroughTcpTransportFactory()
    {
        var port = GetFreePort();
        var serializer = new JsonTestSerializer();
        var handler = new RecordingHandler(ClusterSendStatus.Accepted);
        using var stopServer = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(serializer)
            .UseAcceptor(new TcpConnectionAcceptor(port));
        ULinkRpcClusterMessageBinder.Bind(builder.ServiceRegistry, handler);

        var serverTask = builder.RunAsync(stopServer.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await using var clientFactory = new ULinkRpcClusterClientFactory(
            new TcpULinkRpcClusterTransportFactory(),
            serializer);
        var messenger = new ULinkRpcClusterNodeMessenger(
            clientFactory,
            new ULinkRpcClusterNodeMessengerOptions
            {
                SendTimeout = TimeSpan.FromSeconds(2)
            });

        var status = await messenger.SendAsync(
            new RouteLocation(
                "room/1",
                "node-b",
                new NodeEndpoint($"tcp://127.0.0.1:{port}"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                nodeEpoch: 1,
                generation: 1),
            new ClusterMessage(
                "room/1",
                "command",
                new byte[] { 1, 2, 3 },
                DateTimeOffset.UtcNow.AddMinutes(1),
                "node-a"),
            TestContext.Current.CancellationToken);

        stopServer.Cancel();
        await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(ClusterSendStatus.Accepted, status);
        var message = Assert.Single(handler.Messages);
        Assert.Equal(new RouteKey("room/1"), message.Route);
        Assert.Equal(new NodeId("node-a"), message.SourceNode);
    }

    [Fact]
    public async Task RouteDirectoryCanRegisterResolveRefreshAndClearThroughTcpTransportFactory()
    {
        var port = GetFreePort();
        var serializer = new JsonTestSerializer();
        var directory = new InMemoryRouteDirectory();
        using var stopServer = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(serializer)
            .UseAcceptor(new TcpConnectionAcceptor(port));
        ULinkRpcRouteDirectoryBinder.Bind(builder.ServiceRegistry, directory);

        var serverTask = builder.RunAsync(stopServer.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await using var clientFactory = new ULinkRpcClusterClientFactory(
            new TcpULinkRpcClusterTransportFactory(),
            serializer);
        var client = await clientFactory.GetClientAsync(
            new RouteLocation(
                "directory",
                "directory-node",
                new NodeEndpoint($"tcp://127.0.0.1:{port}"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                nodeEpoch: 1,
                generation: 1),
            TestContext.Current.CancellationToken);
        var remoteDirectory = new ULinkRpcRouteDirectory(client);
        var now = DateTimeOffset.UtcNow;
        var location = new RouteLocation(
            "room/42",
            "node-b",
            new NodeEndpoint($"tcp://127.0.0.1:{port}"),
            now.AddMinutes(1),
            nodeEpoch: 7,
            generation: 11);

        var register = await remoteDirectory.RegisterAsync(location, TestContext.Current.CancellationToken);
        var resolved = await remoteDirectory.ResolveAsync(location.Route, now, TestContext.Current.CancellationToken);
        var refresh = await remoteDirectory.RefreshLeaseAsync(
            location,
            now.AddMinutes(2),
            now,
            TestContext.Current.CancellationToken);
        var stale = await remoteDirectory.RegisterAsync(
            new RouteLocation(
                location.Route,
                location.Node,
                location.Endpoint,
                now.AddMinutes(3),
                nodeEpoch: location.NodeEpoch,
                generation: location.Generation - 1),
            TestContext.Current.CancellationToken);
        var cleared = await remoteDirectory.ClearByNodeEpochAsync(
            location.Node,
            location.NodeEpoch,
            TestContext.Current.CancellationToken);

        stopServer.Cancel();
        await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(RouteRegistrationStatus.Registered, register);
        Assert.NotNull(resolved);
        Assert.Equal(location.Route, resolved.Route);
        Assert.Equal(location.Node, resolved.Node);
        Assert.Equal(location.NodeEpoch, resolved.NodeEpoch);
        Assert.Equal(location.Generation, resolved.Generation);
        Assert.Equal(RouteLeaseRefreshStatus.Refreshed, refresh);
        Assert.Equal(RouteRegistrationStatus.StaleLocation, stale);
        Assert.Equal(1, cleared);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class RecordingHandler : IClusterMessageHandler
    {
        private readonly ClusterSendStatus _status;

        public RecordingHandler(ClusterSendStatus status)
        {
            _status = status;
        }

        public List<ClusterMessage> Messages { get; } = new();

        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return ValueTask.FromResult(_status);
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
}
