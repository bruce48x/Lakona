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
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ReliablePushAckRpcTests
{
    [Fact]
    public async Task Bound_session_raw_internal_ack_returns_ok_with_internal_codec()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ReliablePushAckRpcFixture.StartAsync(cancellationToken);
        await fixture.HandshakeAsync(cancellationToken);
        var server = fixture.Services.GetRequiredService<ILakonaGameServer>();
        var notifications = fixture.Services.GetRequiredService<IClientNotifications>();
        var session = await server.StartSessionAsync(
            "player-a",
            ReliablePushAckRpcFixture.ConnectionId,
            new AckTestCallback(),
            cancellationToken);
        await notifications
            .ForSession(session)
            .NotifyAsync<IAckTestCallback>(
                target => target.NotifyAsync("payload"),
                cancellationToken);

        using var response = await fixture.Client.CallRawAsync(
                GameReliablePushRpcIds.ServiceId,
                GameReliablePushRpcIds.AckMethodId,
                LakonaInternalCodec.EncodeReliablePushAckRequest(
                    new ReliablePushAckRequest(session.SessionId, session.Generation, ReliablePushSequence.From(1))),
                cancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        var outcome = LakonaInternalCodec.DecodeReliablePushAckOutcome(response.Memory);
        Assert.NotEqual(ReliablePushAckStatus.SessionMismatch, outcome.Status);
        Assert.Equal(ReliablePushAckStatus.Accepted, outcome.Status);
        Assert.Equal(0, fixture.EndpointSerializer.CallCount);
    }

    [Fact]
    public async Task Unbound_connection_valid_internal_ack_returns_bad_request_without_endpoint_serializer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ReliablePushAckRpcFixture.StartAsync(cancellationToken);
        await fixture.HandshakeAsync(cancellationToken);

        var failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Client.CallRawAsync(
                    GameReliablePushRpcIds.ServiceId,
                    GameReliablePushRpcIds.AckMethodId,
                    LakonaInternalCodec.EncodeReliablePushAckRequest(
                        new ReliablePushAckRequest("session-a", ReliablePushSequence.From(1))),
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));

        Assert.Equal(RpcStatus.BadRequest, failure.Status);
        Assert.Equal(0, fixture.EndpointSerializer.CallCount);
    }

    private interface IAckTestCallback
    {
        ValueTask NotifyAsync(string payload);
    }

    private sealed class AckTestCallback : IAckTestCallback
    {
        public ValueTask NotifyAsync(string payload)
        {
            return default;
        }
    }

    [Fact]
    public async Task Malformed_internal_ack_payload_returns_bad_request_not_handler_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ReliablePushAckRpcFixture.StartAsync(cancellationToken);
        await fixture.HandshakeAsync(cancellationToken);

        var failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Client.CallRawAsync(
                    GameReliablePushRpcIds.ServiceId,
                    GameReliablePushRpcIds.AckMethodId,
                    new byte[] { 1, 2, 3 },
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));

        Assert.Equal(RpcStatus.BadRequest, failure.Status);
        Assert.NotEqual(RpcStatus.HandlerError, failure.Status);
        Assert.Equal(0, fixture.EndpointSerializer.CallCount);
    }

    private sealed class ReliablePushAckRpcFixture : IAsyncDisposable
    {
        public const string ConnectionId = "loopback-reliable-push";

        private readonly CancellationTokenSource _stopServer;
        private readonly Task _serverTask;

        private ReliablePushAckRpcFixture(
            ServiceProvider services,
            RpcClientRuntime client,
            CountingFrameworkDtoRejectingSerializer endpointSerializer,
            CancellationTokenSource stopServer,
            Task serverTask)
        {
            Services = services;
            Client = client;
            EndpointSerializer = endpointSerializer;
            _stopServer = stopServer;
            _serverTask = serverTask;
        }

        public ServiceProvider Services { get; }

        public RpcClientRuntime Client { get; }

        public CountingFrameworkDtoRejectingSerializer EndpointSerializer { get; }

        public static async ValueTask<ReliablePushAckRpcFixture> StartAsync(CancellationToken cancellationToken)
        {
            var endpointSerializer = new CountingFrameworkDtoRejectingSerializer(new JsonRpcSerializer());
            LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
            var acceptor = new SingleConnectionAcceptor(serverTransport);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Lakona:Node:Id"] = "node-a"
                })
                .Build();
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(LakonaRpcServiceCatalog.FromTypes([]))
                .AddLakonaGameServer(configuration)
                .BuildServiceProvider();

            var builder = RpcServerHostBuilder.Create();
            var endpoint = new LakonaGameEndpointOptions
            {
                Transport = "tcp",
                Serializer = "json",
                ReliablePush = true,
                RpcServices = []
            };
            var configurator = new LakonaEndpointRpcServerConfigurator(endpoint);
            configurator.Configure(new LakonaGameServerRpcContext(
                "test",
                endpoint,
                builder,
                services,
                [],
                cancellationToken));
            builder.UseSerializer(endpointSerializer);
            builder.UseAcceptor(acceptor);

            var host = builder.Build();
            var stopServer = new CancellationTokenSource();
            var serverTask = host.RunAsync(stopServer.Token).AsTask();
            var client = new RpcClientRuntime(clientTransport, endpointSerializer);
            await client.StartAsync(cancellationToken);

            return new ReliablePushAckRpcFixture(
                services,
                client,
                endpointSerializer,
                stopServer,
                serverTask);
        }

        public async ValueTask HandshakeAsync(CancellationToken cancellationToken)
        {
            using var hello = await Client.CallRawAsync(
                    GameHandshakeRpcIds.ServiceId,
                    GameHandshakeRpcIds.HandshakeMethodId,
                    LakonaInternalCodec.EncodeGameClientHello(new GameClientHello
                    {
                        ProtocolVersion = 1
                    }),
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            LakonaInternalCodec.DecodeGameServerHello(hello.Memory);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            _stopServer.Cancel();
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            _stopServer.Dispose();
            await Services.DisposeAsync();
        }
    }

    private sealed class CountingFrameworkDtoRejectingSerializer(IRpcSerializer inner) : IRpcSerializer
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public TransportFrame SerializeFrame<T>(T value)
        {
            CountAndReject<T>();
            return inner.SerializeFrame(value);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data)
        {
            CountAndReject<T>();
            return inner.Deserialize<T>(data);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            CountAndReject<T>();
            return inner.Deserialize<T>(data);
        }

        private void CountAndReject<T>()
        {
            Interlocked.Increment(ref _callCount);
            var type = typeof(T);
            if (type == typeof(ReliablePushAckRequest) ||
                type == typeof(ReliablePushAckOutcome))
            {
                throw new InvalidOperationException("Reliable push ack DTOs must use LakonaInternalCodec.");
            }
        }
    }

    private sealed class SingleConnectionAcceptor(ITransport transport) : IRpcConnectionAcceptor
    {
        private int _accepted;

        public string ListenAddress => "loopback://reliable-push-ack-test";

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                await transport.ConnectAsync(ct).ConfigureAwait(false);
                return new RpcAcceptedConnection(transport, ReliablePushAckRpcFixture.ConnectionId);
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
