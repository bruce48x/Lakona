using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameHandshakeGateTests
{
    [Fact]
    public async Task Raw_handshake_with_unsupported_protocol_returns_bad_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new FrameworkDtoRejectingSerializer(new JsonRpcSerializer());
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var acceptor = new SingleConnectionAcceptor(serverTransport);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "node-a"
            })
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        var builder = RpcServerHostBuilder.Create();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = []
        };
        var configurator = new LakonaEndpointRpcServerConfigurator(endpoint);
        configurator.Configure(new LakonaGameServerRpcContext(
            "test",
            endpoint,
            builder,
            provider,
            [],
            cancellationToken));
        builder.UseAcceptor(acceptor);

        var host = builder.Build();
        using var stopServer = new CancellationTokenSource();
        var serverTask = host.RunAsync(stopServer.Token).AsTask();
        await using var client = new RpcClientRuntime(clientTransport, serializer);
        await client.StartAsync(cancellationToken);

        try
        {
            var helloPayload = LakonaInternalCodec.EncodeGameClientHello(
                new GameClientHello
                {
                    ProtocolVersion = 2
                });

            var failure = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.CallRawAsync(
                        GameHandshakeRpcIds.ServiceId,
                        GameHandshakeRpcIds.HandshakeMethodId,
                        helloPayload,
                        cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));

            Assert.Equal(RpcStatus.BadRequest, failure.Status);
            Assert.Equal("Client does not support Lakona game handshake protocol version 1.", failure.ErrorMessage);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    [Fact]
    public async Task Handshake_service_failure_returns_handler_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new FrameworkDtoRejectingSerializer(new JsonRpcSerializer());
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var acceptor = new SingleConnectionAcceptor(serverTransport);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "node-a"
            })
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddSingleton<IGameHandshakeService, ThrowingHandshakeService>()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        var builder = RpcServerHostBuilder.Create();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = []
        };
        var configurator = new LakonaEndpointRpcServerConfigurator(endpoint);
        configurator.Configure(new LakonaGameServerRpcContext(
            "test",
            endpoint,
            builder,
            provider,
            [],
            cancellationToken));
        builder.UseAcceptor(acceptor);

        var host = builder.Build();
        using var stopServer = new CancellationTokenSource();
        var serverTask = host.RunAsync(stopServer.Token).AsTask();
        await using var client = new RpcClientRuntime(clientTransport, serializer);
        await client.StartAsync(cancellationToken);

        try
        {
            var helloPayload = LakonaInternalCodec.EncodeGameClientHello(
                new GameClientHello
                {
                    ProtocolVersion = 1
                });

            var failure = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.CallRawAsync(
                        GameHandshakeRpcIds.ServiceId,
                        GameHandshakeRpcIds.HandshakeMethodId,
                        helloPayload,
                        cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));

            Assert.Equal(RpcStatus.HandlerError, failure.Status);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    [Fact]
    public async Task Heartbeat_invalid_server_reply_returns_handler_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new FrameworkDtoRejectingSerializer(new JsonRpcSerializer());
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var acceptor = new SingleConnectionAcceptor(serverTransport);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "node-a"
            })
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddSingleton<IGameHeartbeatService, InvalidHeartbeatService>()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        var builder = RpcServerHostBuilder.Create();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = []
        };
        var configurator = new LakonaEndpointRpcServerConfigurator(endpoint);
        configurator.Configure(new LakonaGameServerRpcContext(
            "test",
            endpoint,
            builder,
            provider,
            [],
            cancellationToken));
        builder.UseAcceptor(acceptor);

        var host = builder.Build();
        using var stopServer = new CancellationTokenSource();
        var serverTask = host.RunAsync(stopServer.Token).AsTask();
        await using var client = new RpcClientRuntime(clientTransport, serializer);
        await client.StartAsync(cancellationToken);

        try
        {
            var helloPayload = LakonaInternalCodec.EncodeGameClientHello(
                new GameClientHello
                {
                    ProtocolVersion = 1
                });
            using var _ = await client.CallRawAsync(
                    GameHandshakeRpcIds.ServiceId,
                    GameHandshakeRpcIds.HandshakeMethodId,
                    helloPayload,
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            var heartbeatPayload = LakonaInternalCodec.EncodeGameHeartbeatRequest(new GameHeartbeatRequest());
            var failure = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.CallRawAsync(
                        GameHeartbeatRpcIds.ServiceId,
                        GameHeartbeatRpcIds.HeartbeatMethodId,
                        heartbeatPayload,
                        cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));

            Assert.Equal(RpcStatus.HandlerError, failure.Status);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    [Fact]
    public async Task Business_rpc_is_rejected_before_handshake_and_allowed_after_handshake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new FrameworkDtoRejectingSerializer(new JsonRpcSerializer());
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var acceptor = new SingleConnectionAcceptor(serverTransport);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "node-a"
            })
            .Build();
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        var builder = RpcServerHostBuilder.Create();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = []
        };
        var configurator = new LakonaEndpointRpcServerConfigurator(
            endpoint,
            static (registry, _) => registry.Register(
                serviceId: 10,
                methodId: 1,
                static (session, request, _) =>
                {
                    var value = session.Serializer.Deserialize<string>(request.Payload.Memory);
                    using var payload = session.Serializer.SerializeFrame(value + ":ok");
                    return ValueTask.FromResult(RpcEnvelopeCodec.EncodeResponse(
                        request.RequestId,
                        RpcStatus.Ok,
                        payload.Memory));
                }));
        configurator.Configure(new LakonaGameServerRpcContext(
            "test",
            endpoint,
            builder,
            provider,
            [],
            cancellationToken));
        builder.UseAcceptor(acceptor);

        var host = builder.Build();
        using var stopServer = new CancellationTokenSource();
        var serverTask = host.RunAsync(stopServer.Token).AsTask();
        await using var client = new RpcClientRuntime(clientTransport, serializer);
        await client.StartAsync(cancellationToken);

        try
        {
            var before = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.CallAsync(new RpcMethod<string, string>(10, 1), "before", cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
            Assert.Equal(RpcStatus.BadRequest, before.Status);
            Assert.Equal("HandshakeRequired", before.ErrorMessage);

            var heartbeatBeforePayload = LakonaInternalCodec.EncodeGameHeartbeatRequest(new GameHeartbeatRequest());
            var heartbeatBefore = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.CallRawAsync(
                        GameHeartbeatRpcIds.ServiceId,
                        GameHeartbeatRpcIds.HeartbeatMethodId,
                        heartbeatBeforePayload,
                        cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
            Assert.Equal(RpcStatus.BadRequest, heartbeatBefore.Status);
            Assert.Equal("HandshakeRequired", heartbeatBefore.ErrorMessage);

            var helloPayload = LakonaInternalCodec.EncodeGameClientHello(
                new GameClientHello
                {
                    ProtocolVersion = 1,
                    ResumeTicket = "unknown-framework-ticket"
                });
            using var helloFrame = await client.CallRawAsync(
                    GameHandshakeRpcIds.ServiceId,
                    GameHandshakeRpcIds.HandshakeMethodId,
                    helloPayload,
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            var hello = LakonaInternalCodec.DecodeGameServerHello(helloFrame.Memory);

            Assert.Equal(1, hello.SelectedProtocolVersion);
            Assert.Equal(GameSessionRecoveryStatus.StateLost, hello.Recovery.Status);

            var heartbeatPayload = LakonaInternalCodec.EncodeGameHeartbeatRequest(new GameHeartbeatRequest());
            using var heartbeatFrame = await client.CallRawAsync(
                    GameHeartbeatRpcIds.ServiceId,
                    GameHeartbeatRpcIds.HeartbeatMethodId,
                    heartbeatPayload,
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            var heartbeat = LakonaInternalCodec.DecodeGameHeartbeatReply(heartbeatFrame.Memory);

            Assert.Equal(GameHeartbeatStatus.Ok, heartbeat.Status);

            var after = await client.CallAsync(new RpcMethod<string, string>(10, 1), "after", cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            Assert.Equal("after:ok", after);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private sealed class ThrowingHandshakeService : IGameHandshakeService
    {
        public ValueTask<GameServerHello> HandshakeAsync(
            GameClientHello hello,
            string endpointTransport,
            string endpointSerializer,
            bool reliablePush,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handshake service failed.");
        }
    }

    private sealed class InvalidHeartbeatService : IGameHeartbeatService
    {
        public ValueTask<GameHeartbeatReply> HeartbeatAsync(
            string connectionId,
            GameHeartbeatRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameHeartbeatReply>(new GameHeartbeatReply
            {
                Status = (GameHeartbeatStatus)999
            });
        }
    }

    private sealed class FrameworkDtoRejectingSerializer(IRpcSerializer inner) : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value)
        {
            RejectFrameworkDto<T>();
            return inner.SerializeFrame(value);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data)
        {
            RejectFrameworkDto<T>();
            return inner.Deserialize<T>(data);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            RejectFrameworkDto<T>();
            return inner.Deserialize<T>(data);
        }

        private static void RejectFrameworkDto<T>()
        {
            var type = typeof(T);
            if (type == typeof(GameClientHello) ||
                type == typeof(GameServerHello) ||
                type == typeof(GameHeartbeatRequest) ||
                type == typeof(GameHeartbeatReply))
            {
                throw new InvalidOperationException("Framework DTOs must use LakonaInternalCodec.");
            }
        }
    }

    private sealed class SingleConnectionAcceptor(ITransport transport) : IRpcConnectionAcceptor
    {
        private int _accepted;

        public string ListenAddress => "loopback://handshake-test";

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                await transport.ConnectAsync(ct).ConfigureAwait(false);
                return new RpcAcceptedConnection(transport, "loopback");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
