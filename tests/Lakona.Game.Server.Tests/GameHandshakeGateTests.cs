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
    public async Task Server_termination_notifies_disconnects_and_releases_endpoint_capacity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new FrameworkDtoRejectingSerializer(new JsonRpcSerializer());
        LoopbackTransport.CreatePair(out var firstClientTransport, out var firstServerTransport);
        LoopbackTransport.CreatePair(out var secondClientTransport, out var secondServerTransport);
        await using var acceptor = new GatedConnectionAcceptor(firstServerTransport, secondServerTransport);
        var lifecycle = new RecordingRpcSessionLifecycleObserver();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "node-a"
            })
            .Build();
        var services = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddSingleton<IRpcSessionLifecycleObserver>(lifecycle)
            .AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer(configuration);
        services.UseReadySingleNodeMembership("node-a");
        await using var provider = services.BuildServiceProvider();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = [],
            ConnectionLimits = new LakonaGameEndpointConnectionLimitsOptions
            {
                MaxActiveConnections = 1,
                MaxPendingHandshakes = 1,
                HandshakeTimeout = TimeSpan.FromSeconds(5)
            }
        };
        var builder = RpcServerHostBuilder.Create();
        new LakonaEndpointRpcServerConfigurator(endpoint).Configure(
            new LakonaGameServerRpcContext(
                "test",
                endpoint,
                builder,
                provider,
                [],
                cancellationToken));
        builder.UseAcceptor(acceptor);
        using var stopServer = new CancellationTokenSource();
        var serverTask = builder.Build().RunAsync(stopServer.Token).AsTask();
        await using var firstClient = new RpcClientRuntime(firstClientTransport, serializer);
        var firstDisconnected = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        firstClient.Disconnected += error => firstDisconnected.TrySetResult(error);
        var terminationNotice = new TaskCompletionSource<SessionTerminationNotice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        firstClient.RegisterRawNotificationHandler(
            GameSessionNotificationRpcIds.ServiceId,
            GameSessionNotificationRpcIds.TerminatedNotificationId,
            payload =>
            {
                terminationNotice.TrySetResult(
                    LakonaInternalCodec.DecodeSessionTerminationNotice(payload));
                return default;
            });

        try
        {
            await firstClient.StartAsync(cancellationToken);
            await CompleteHandshakeAsync(firstClient, cancellationToken);
            var firstConnectionId = await lifecycle.FirstStarted.Task
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            var session = await provider.GetRequiredService<ILakonaGameServer>()
                .StartSessionAsync("player-a", firstConnectionId, cancellationToken);

            await provider.GetRequiredService<ILakonaGameServer>().TerminateSessionAsync(
                session,
                SessionTerminationReason.Policy,
                message: "Removed by server policy.",
                options: new SessionTerminationOptions
                {
                    NotifyTimeout = TimeSpan.FromSeconds(1),
                    KeepTerminalStateForResume = false
                },
                cancellationToken: cancellationToken);

            var notice = await terminationNotice.Task
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            await firstDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            await lifecycle.FirstDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            acceptor.ReleaseSecond();
            await using var secondClient = new RpcClientRuntime(secondClientTransport, serializer);
            await secondClient.StartAsync(cancellationToken);
            var hello = await CompleteHandshakeAsync(secondClient, cancellationToken);

            Assert.Equal(SessionTerminationReason.Policy, notice.Reason);
            Assert.Equal("Removed by server policy.", notice.Message);
            Assert.Equal(1, hello.SelectedProtocolVersion);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }


    [Fact]
    public async Task Retained_termination_is_reported_by_the_framework_recovery_handshake()
    {
        var services = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddLogging()
            .AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);
        var tickets = provider.GetRequiredService<IGameSessionResumeTicketStore>();
        var ticket = await tickets.IssueAsync(
            session,
            "legacy",
            TestContext.Current.CancellationToken);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            options: new SessionTerminationOptions
            {
                NotifyTimeout = TimeSpan.Zero,
                KeepTerminalStateForResume = true
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var recovery = await provider.GetRequiredService<IGameSessionHandshakeRecoveryService>()
            .RecoverAsync(
                ticket,
                "connection-b",
                "legacy",
                TestContext.Current.CancellationToken);

        Assert.Equal(GameSessionRecoveryStatus.Terminated, recovery.Status);
    }

    [Fact]
    public async Task Connection_without_game_handshake_is_closed_at_endpoint_deadline()
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
            .AddTestEndpointRuntimes()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = [],
            ConnectionLimits = new LakonaGameEndpointConnectionLimitsOptions
            {
                MaxActiveConnections = 2,
                MaxPendingHandshakes = 1,
                HandshakeTimeout = TimeSpan.FromMilliseconds(50)
            }
        };
        var builder = RpcServerHostBuilder.Create();
        new LakonaEndpointRpcServerConfigurator(endpoint).Configure(
            new LakonaGameServerRpcContext(
                "test",
                endpoint,
                builder,
                provider,
                [],
                cancellationToken));
        builder.UseAcceptor(acceptor);
        using var stopServer = new CancellationTokenSource();
        var serverTask = builder.Build().RunAsync(stopServer.Token).AsTask();
        await using var client = new RpcClientRuntime(clientTransport, serializer);
        var disconnected = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += error => disconnected.TrySetResult(error);

        try
        {
            await client.StartAsync(cancellationToken);
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    [Fact]
    public async Task Endpoint_rejects_connections_beyond_pending_handshake_budget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new FrameworkDtoRejectingSerializer(new JsonRpcSerializer());
        LoopbackTransport.CreatePair(out var firstClientTransport, out var firstServerInnerTransport);
        LoopbackTransport.CreatePair(out var rejectedClientTransport, out var rejectedServerInnerTransport);
        var firstServerTransport = new TrackingTransport(firstServerInnerTransport);
        var rejectedServerTransport = new TrackingTransport(rejectedServerInnerTransport);
        await using var acceptor = new QueueConnectionAcceptor(
            firstServerTransport,
            rejectedServerTransport);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "node-a"
            })
            .Build();
        await using var provider = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = [],
            ConnectionLimits = new LakonaGameEndpointConnectionLimitsOptions
            {
                MaxActiveConnections = 2,
                MaxPendingHandshakes = 1,
                HandshakeTimeout = TimeSpan.FromSeconds(5)
            }
        };
        var builder = RpcServerHostBuilder.Create();
        new LakonaEndpointRpcServerConfigurator(endpoint).Configure(
            new LakonaGameServerRpcContext(
                "test",
                endpoint,
                builder,
                provider,
                [],
                cancellationToken));
        builder.UseAcceptor(acceptor);
        using var stopServer = new CancellationTokenSource();
        var serverTask = builder.Build().RunAsync(stopServer.Token).AsTask();
        await using var firstClient = new RpcClientRuntime(firstClientTransport, serializer);
        await using var rejectedClient = new RpcClientRuntime(rejectedClientTransport, serializer);
        var rejected = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        rejectedClient.Disconnected += error => rejected.TrySetResult(error);

        try
        {
            await firstClient.StartAsync(cancellationToken);
            await rejectedClient.StartAsync(cancellationToken);
            await rejected.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            Assert.True(rejectedServerTransport.Disposed.Task.IsCompletedSuccessfully);
            Assert.False(firstServerTransport.Disposed.Task.IsCompleted);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

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
            .AddTestEndpointRuntimes()
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
            .AddTestEndpointRuntimes()
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

            Assert.Equal(RpcStatus.InternalError, failure.Status);
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
            .AddTestEndpointRuntimes()
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

            Assert.Equal(RpcStatus.InternalError, failure.Status);
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
            .AddTestEndpointRuntimes()
            .AddLogging()
            .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        var builder = RpcServerHostBuilder.Create();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "tcp",
            Serializer = "json",
            RpcServices = [],
            ConnectionLimits = new LakonaGameEndpointConnectionLimitsOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(5)
            }
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

    private static async ValueTask<GameServerHello> CompleteHandshakeAsync(
        RpcClientRuntime client,
        CancellationToken cancellationToken)
    {
        var payload = LakonaInternalCodec.EncodeGameClientHello(
            new GameClientHello { ProtocolVersion = 1 });
        using var frame = await client.CallRawAsync(
                GameHandshakeRpcIds.ServiceId,
                GameHandshakeRpcIds.HandshakeMethodId,
                payload,
                cancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        return LakonaInternalCodec.DecodeGameServerHello(frame.Memory);
    }

    private sealed class RecordingRpcSessionLifecycleObserver : IRpcSessionLifecycleObserver
    {
        public TaskCompletionSource<string> FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstDisconnected { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask OnSessionStartedAsync(
            RpcSessionLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            FirstStarted.TrySetResult(context.ConnectionId);
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(
            RpcSessionLifecycleContext context,
            Exception? error,
            CancellationToken cancellationToken = default)
        {
            FirstDisconnected.TrySetResult();
            return default;
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
        public void Serialize<T>(
            System.Buffers.IBufferWriter<byte> destination,
            T value)
        {
            RejectFrameworkDto<T>();
            inner.Serialize(destination, value);
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

    private sealed class QueueConnectionAcceptor(params ITransport[] transports) : IRpcConnectionAcceptor
    {
        private readonly Queue<ITransport> _transports = new(transports);

        public string ListenAddress => "loopback://queue";

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (_transports.Count > 0)
                return new RpcAcceptedConnection(_transports.Dequeue(), "loopback");

            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable acceptor continuation.");
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    private sealed class GatedConnectionAcceptor(
        ITransport first,
        ITransport second) : IRpcConnectionAcceptor
    {
        private readonly TaskCompletionSource releaseSecond =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int accepted;

        public string ListenAddress => "loopback://gated";

        public void ReleaseSecond()
        {
            releaseSecond.TrySetResult();
        }

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            var index = Interlocked.Increment(ref accepted);
            if (index == 1)
            {
                await first.ConnectAsync(ct).ConfigureAwait(false);
                return new RpcAcceptedConnection(first, "first-loopback");
            }
            if (index == 2)
            {
                await releaseSecond.Task.WaitAsync(ct).ConfigureAwait(false);
                await second.ConnectAsync(ct).ConfigureAwait(false);
                return new RpcAcceptedConnection(second, "second-loopback");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable acceptor continuation.");
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    private sealed class TrackingTransport(ITransport inner) : ITransport
    {
        private int _disposed;

        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            return inner.ConnectAsync(ct);
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return inner.SendFrameAsync(frame, ct);
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            return inner.ReceiveFrameAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            await inner.DisposeAsync();
            Disposed.TrySetResult();
        }
    }
}
