using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaEndpointRuntimeDefaultsTests
{
    [Theory]
    [InlineData("json", typeof(JsonRpcSerializer))]
    [InlineData("memorypack", typeof(MemoryPackRpcSerializer))]
    public void CreateSerializer_uses_endpoint_local_serializer(string serializer, Type expectedType)
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "websocket",
            Serializer = serializer,
            Host = "127.0.0.1",
            Port = 20000,
            Path = "/ws"
        };

        var result = LakonaEndpointRuntimeDefaults.CreateSerializer(endpoint);

        Assert.IsType(expectedType, result);
    }

    [Fact]
    public void CreateSerializer_rejects_unknown_serializer()
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "websocket",
            Serializer = "protobuf",
            Host = "127.0.0.1",
            Port = 20000,
            Path = "/ws"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaEndpointRuntimeDefaults.CreateSerializer(endpoint));

        Assert.Contains("protobuf", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
