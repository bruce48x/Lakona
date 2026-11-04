using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        Assert.Same(endpointSerializer, runtime.GetEndpointSerializer(endpoint.Serializer));
        Assert.Same(acceptor, await runtime.CreateAcceptorAsync(
            endpoint,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Worker_without_listeners_uses_the_gateways_registered_wire_options()
    {
        static IRpcSerializer CreateNotificationSerializer()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter<DayOfWeek>(JsonNamingPolicy.CamelCase));
            return new JsonRpcSerializer(options);
        }

        var services = new ServiceCollection()
            .AddLakonaEndpointSerializer("json", static () => new JsonRpcSerializer())
            .AddLakonaEndpointSerializer("calendar-v2", CreateNotificationSerializer);
        using var gateway = services.BuildServiceProvider();
        using var worker = services.BuildServiceProvider();
        var gatewayRegistry = gateway.GetRequiredService<LakonaEndpointRuntimeRegistry>();
        var endpoint = new LakonaGameEndpointOptions { Serializer = " Calendar-V2 " };
        var serializer = gatewayRegistry.GetEndpointSerializer(endpoint.Serializer);
        var profile = LakonaEndpointRuntimeRegistry.NormalizeSerializerProfile(endpoint.Serializer);
        Assert.Equal("calendar-v2", profile);
        Assert.Same(serializer, gatewayRegistry.GetEndpointSerializer(profile));

        var buffer = new ArrayBufferWriter<byte>();
        var workerRegistry = worker.GetRequiredService<LakonaEndpointRuntimeRegistry>();
        workerRegistry.GetEndpointSerializer(profile).Serialize(buffer, DayOfWeek.Monday);
        Assert.Equal("\"monday\"", Encoding.UTF8.GetString(buffer.WrittenSpan));
        Assert.Equal(DayOfWeek.Monday, serializer.Deserialize<DayOfWeek>(buffer.WrittenMemory));
        Assert.Throws<JsonException>(() => gatewayRegistry.GetEndpointSerializer("json").Deserialize<DayOfWeek>(buffer.WrittenMemory));
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
            runtime.GetEndpointSerializer(endpoint.Serializer));

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
