using System.Reflection;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerBuilderClusterRpcTests
{
    [Fact]
    public void Builder_public_surface_contains_only_application_owned_extension_points()
    {
        var actual = typeof(LakonaGameServerBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(FormatSignature)
            .Order(StringComparer.Ordinal);
        var expected = new[]
        {
            FormatSignature(nameof(LakonaGameServerBuilder.AddServices), typeof(Action<IServiceCollection>)),
            FormatSignature(nameof(LakonaGameServerBuilder.AddServices), typeof(Action<IServiceCollection, IConfiguration>)),
            FormatSignature(nameof(LakonaGameServerBuilder.BindServices), typeof(Action<RpcServiceRegistry>)),
            FormatSignature(nameof(LakonaGameServerBuilder.BindServices), typeof(Action<RpcServiceRegistry, IServiceProvider>)),
            FormatSignature(nameof(LakonaGameServerBuilder.ConfigureAppConfiguration), typeof(Action<IConfigurationBuilder>)),
            FormatSignature(nameof(LakonaGameServerBuilder.ConfigureLogging), typeof(Action<ILoggingBuilder>)),
            FormatSignature(
                nameof(LakonaGameServerBuilder.RegisterEndpointSerializer),
                typeof(string),
                typeof(Func<IRpcSerializer>)),
            FormatSignature(
                nameof(LakonaGameServerBuilder.RegisterEndpointTransport),
                typeof(string),
                typeof(Func<LakonaGameEndpointOptions, IRpcConnectionAcceptor>)),
            FormatSignature(
                nameof(LakonaGameServerBuilder.RegisterEndpointTransport),
                typeof(string),
                typeof(Func<
                    LakonaGameEndpointOptions,
                    CancellationToken,
                    ValueTask<IRpcConnectionAcceptor>>))
        }.Order(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Cluster_endpoint_registers_the_builtin_tcp_memorypack_channel()
    {
        var hostBuilder = Host.CreateApplicationBuilder([]);
        hostBuilder.Services.AddLakonaGameServer();

        using var provider = hostBuilder.Services.BuildServiceProvider();
        var channel = provider.GetRequiredService<ClusterRpcChannel>();
        Assert.Equal("tcp", channel.TransportScheme);
        Assert.IsType<MemoryPackRpcSerializer>(channel.Serializer);
    }

    private static string FormatSignature(MethodInfo method) =>
        FormatSignature(
            method.Name,
            method.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());

    private static string FormatSignature(string name, params Type[] parameterTypes) =>
        $"{name}({string.Join(",", parameterTypes.Select(static type => type.FullName))})";
}
