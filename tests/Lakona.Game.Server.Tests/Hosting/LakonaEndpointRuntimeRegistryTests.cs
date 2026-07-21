using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaEndpointRuntimeRegistryTests
{
    [Fact]
    public async Task Explicit_runtime_registrations_are_selected_by_configuration_name()
    {
        var endpointSerializer = new JsonRpcSerializer();
        var acceptor = new StubConnectionAcceptor();
        var services = new ServiceCollection()
            .AddLakonaEndpointTransport("custom", _ => acceptor)
            .AddLakonaEndpointSerializer("custom", () => endpointSerializer);

        await using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<LakonaEndpointRuntimeRegistry>();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "CUSTOM",
            Serializer = " custom ",
            Host = "127.0.0.1",
            Port = 20000
        };

        Assert.Same(endpointSerializer, runtime.CreateEndpointSerializer(endpoint));
        Assert.Same(acceptor, await runtime.CreateAcceptorAsync(
            endpoint,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Endpoint_reliable_push_is_disabled_unless_explicitly_enabled()
    {
        Assert.False(new LakonaGameEndpointOptions().ReliablePush);
        Assert.True(new LakonaGameEndpointOptions { ReliablePush = true }.ReliablePush);
    }

    [Fact]
    public void Unregistered_endpoint_serializer_is_rejected_with_configuration_name()
    {
        var services = new ServiceCollection()
            .AddLakonaEndpointSerializer("json", static () => new JsonRpcSerializer());
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<LakonaEndpointRuntimeRegistry>();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "websocket",
            Serializer = "protobuf",
            Host = "127.0.0.1",
            Port = 20000,
            Path = "/ws"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            runtime.CreateEndpointSerializer(endpoint));

        Assert.Contains("protobuf", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubConnectionAcceptor : IRpcConnectionAcceptor
    {
        public string ListenAddress => "custom://127.0.0.1:20000";

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
