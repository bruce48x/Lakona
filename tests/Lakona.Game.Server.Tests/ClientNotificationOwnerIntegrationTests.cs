using System.Net;
using System.Net.Sockets;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationOwnerIntegrationTests
{
    [Fact]
    public async Task Gateway_assigns_one_sequence_stream_to_local_and_remote_notifications()
    {
        var port = GetFreePort();
        var gatewayServices = new ServiceCollection();
        gatewayServices.AddLakonaGameServerSessions();
        gatewayServices.AddLakonaGameServerReliablePush();
        await using var gateway = gatewayServices.BuildServiceProvider();
        var sessions = gateway.GetRequiredService<IGameSessionRegistry>();
        var session = await sessions.StartNewSessionAsync(
            "player-1",
            TestContext.Current.CancellationToken);
        await sessions.SetReliablePushPolicyAsync(
            session,
            true,
            TestContext.Current.CancellationToken);
        var callback = new SequenceCapturingDispatchTarget();
        await sessions.BindSessionAsync<ITestPlayerCallback>(
            session,
            "control-1",
            callback,
            TestContext.Current.CancellationToken);
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint($"tcp://127.0.0.1:{port}"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: session.Generation),
            TestContext.Current.CancellationToken);

        var localStatus = await gateway.GetRequiredService<IClientNotifications>()
            .ForSession(session)
            .NotifyAsync<ITestPlayerCallback>(
                target =>
                {
                    target.Notify("queued");
                    return default;
                },
                TestContext.Current.CancellationToken);

        using var stop = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new TcpConnectionAcceptor(port, "127.0.0.1"));
        var ownerDispatcher = new ClientNotificationOwnerDispatcher(
            gateway.GetRequiredService<IReliablePushRuntime>(),
            routes,
            new NodeId("gateway-1"));
        ClientNotificationCommandBinder.BindOwned(builder.ServiceRegistry, ownerDispatcher);
        var serverTask = builder.RunAsync(stop.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        ClientNotificationStatus remoteStatus;
        try
        {
            await using var clients = new ClusterClientFactory(
                new TcpClusterTransportFactory(),
                new JsonRpcSerializer());
            var remote = new ClusterClientNotificationDispatcher(clients);
            var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
                session,
                target => target.Notify("matched"))!;
            command.Metadata = new RpcPushMetadata
            {
                Type = "untrusted",
                Payload = new byte[] { 9 }
            };

            remoteStatus = await remote.DispatchAsync(
                new RouteLocation(
                    ClientNotificationRouteKey.FromSession(session),
                    new NodeId("gateway-1"),
                    new NodeEndpoint($"tcp://127.0.0.1:{port}"),
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    generation: session.Generation),
                command,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAny(
                serverTask,
                Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }

        var ack = await gateway.GetRequiredService<IReliablePushRuntime>().AckAsync(
            session,
            session,
            2,
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, localStatus);
        Assert.Equal(ClientNotificationStatus.Delivered, remoteStatus);
        Assert.Equal([1L, 2L], callback.Sequences);
        Assert.Equal(["queued", "matched"], callback.Messages);
        Assert.Equal(ReliablePushAckStatus.Accepted, ack.Status);
    }

    private interface ITestPlayerCallback
    {
        void Notify(string message);
    }

    private sealed class SequenceCapturingDispatchTarget :
        ITestPlayerCallback,
        IRpcNotificationDispatchTarget
    {
        public List<long> Sequences { get; } = [];

        public List<string> Messages { get; } = [];

        public void Notify(string message)
        {
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(nameof(ITestPlayerCallback.Notify), methodName);
            Assert.NotNull(metadata);
            var reliable = LakonaInternalCodec.DecodeReliablePushMetadata(metadata.Payload);
            Sequences.Add(reliable.Sequence.Value);
            Messages.Add(Assert.IsType<string>(arguments[0]));
            return default;
        }
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
}
