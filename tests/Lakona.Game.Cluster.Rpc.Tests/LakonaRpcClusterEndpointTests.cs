using Lakona.Game.Cluster.Rpc;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ULinkRpcClusterEndpointTests
{
    [Theory]
    [InlineData("tcp://127.0.0.1:20010", "tcp", "127.0.0.1", 20010, "")]
    [InlineData("ws://localhost:20011/cluster", "websocket", "localhost", 20011, "/cluster")]
    [InlineData("localhost:20012", "tcp", "localhost", 20012, "")]
    public void ParseEndpointAddress(
        string address,
        string expectedScheme,
        string expectedHost,
        int expectedPort,
        string expectedPath)
    {
        var endpoint = ULinkRpcClusterEndpoint.Parse(address);

        Assert.Equal(expectedScheme, endpoint.Scheme);
        Assert.Equal(expectedHost, endpoint.Host);
        Assert.Equal(expectedPort, endpoint.Port);
        Assert.Equal(expectedPath, endpoint.Path);
    }
}
