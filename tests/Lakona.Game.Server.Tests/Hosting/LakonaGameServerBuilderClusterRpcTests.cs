using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerBuilderClusterRpcTests
{
    [Fact]
    public void EnsureClusterRpcConfigured_rejects_an_incomplete_composition_root()
    {
        var hostBuilder = Host.CreateApplicationBuilder([]);
        var builder = new LakonaGameServerBuilder(hostBuilder);

        var exception = Assert.Throws<InvalidOperationException>(builder.EnsureClusterRpcConfigured);

        Assert.Contains("UseClusterRpc", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseClusterRpc_selects_one_transport_and_serializer_for_the_cluster_channel()
    {
        var hostBuilder = Host.CreateApplicationBuilder([]);
        var builder = new LakonaGameServerBuilder(hostBuilder);
        var transport = new StubClusterTransport();
        var serializer = new StubClusterSerializer();

        builder.UseClusterRpc(new StubClusterTransport(), new StubClusterSerializer());
        builder.UseClusterRpc(transport, serializer);
        builder.EnsureClusterRpcConfigured();
        builder.ApplyToHostBuilder();

        using var provider = hostBuilder.Services.BuildServiceProvider();
        Assert.Same(transport, Assert.Single(provider.GetServices<IClusterRpcTransport>()));
        Assert.Same(serializer, Assert.Single(provider.GetServices<IClusterRpcSerializer>()));
        var channel = provider.GetRequiredService<ClusterRpcChannel>();
        Assert.Same(transport, channel.Transport);
        Assert.Same(serializer, channel.SerializerAdapter);
    }

    private sealed class StubClusterTransport : IClusterRpcTransport
    {
        public string Scheme => "stub";

        public ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubClusterSerializer : IClusterRpcSerializer
    {
        public string ProtocolId => "lakona.cluster.stub.v1";

        public IRpcSerializer CreateSerializer() => new JsonRpcSerializer();
    }
}
