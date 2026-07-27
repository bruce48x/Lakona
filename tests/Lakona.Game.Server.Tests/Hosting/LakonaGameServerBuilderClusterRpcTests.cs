using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerBuilderClusterRpcTests
{
    [Fact]
    public void Builder_does_not_expose_cluster_rpc_transport_or_serializer_selection()
    {
        Assert.DoesNotContain(
            typeof(LakonaGameServerBuilder).GetMethods(),
            static method => method.Name == "UseClusterRpc");
    }

    [Fact]
    public void Cluster_endpoint_registers_the_builtin_tcp_memorypack_channel()
    {
        var hostBuilder = Host.CreateApplicationBuilder([]);
        hostBuilder.Services.AddLakonaGameClusterEndpoint();

        using var provider = hostBuilder.Services.BuildServiceProvider();
        var channel = provider.GetRequiredService<ClusterRpcChannel>();
        Assert.Equal("tcp", channel.TransportScheme);
        Assert.IsType<MemoryPackRpcSerializer>(channel.Serializer);
    }
}
