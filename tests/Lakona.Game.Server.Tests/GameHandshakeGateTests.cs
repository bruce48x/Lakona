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
    public async Task Business_rpc_is_rejected_before_handshake_and_allowed_after_handshake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serializer = new JsonRpcSerializer();
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

            var hello = await client.CallAsync(
                    new RpcMethod<GameClientHello, GameServerHello>(
                        GameHandshakeRpc.ServiceId,
                        GameHandshakeRpc.HandshakeMethodId),
                    new GameClientHello
                    {
                        ProtocolVersionMin = 1,
                        ProtocolVersionMax = 1,
                        ClientRuntime = "dotnet"
                    },
                    cancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            Assert.Equal(1, hello.SelectedProtocolVersion);

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
