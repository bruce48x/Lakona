using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaRpcServiceCatalogTests
{
    [Fact]
    public void DiscoversBinderByExplicitAttribute()
    {
        var catalog = LakonaRpcServiceCatalog.FromTypes([typeof(LoginBinder)]);

        Assert.True(catalog.TryGet("login", out var descriptor));
        Assert.Equal(typeof(LoginBinder), descriptor.BinderType);
    }

    [Fact]
    public void RejectsBinderWithoutAttribute()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaRpcServiceCatalog.FromTypes([typeof(MissingAttributeBinder)]));

        Assert.Contains(nameof(MissingAttributeBinder), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateBinderNames()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaRpcServiceCatalog.FromTypes([typeof(LoginBinder), typeof(DuplicateLoginBinder)]));

        Assert.Contains("login", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndpointConfiguratorBindsOnlyEndpointListedServices()
    {
        BoundServices.Clear();
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "websocket",
            Serializer = "json",
            RpcServices = ["login"]
        };
        var catalog = LakonaRpcServiceCatalog.FromTypes([typeof(LoginBinder), typeof(RoomBinder)]);

        ConfigureEndpoint(endpoint, catalog);

        Assert.Equal(["login:websocket"], BoundServices);
    }

    [Fact]
    public void EndpointConfiguratorRejectsUnknownConfiguredService()
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "websocket",
            Serializer = "json",
            RpcServices = ["missing"]
        };
        var catalog = LakonaRpcServiceCatalog.FromTypes([typeof(LoginBinder)]);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigureEndpoint(endpoint, catalog));

        Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndpointConfiguratorRejectsDuplicateConfiguredService()
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "websocket",
            Serializer = "json",
            RpcServices = ["login", "LOGIN"]
        };
        var catalog = LakonaRpcServiceCatalog.FromTypes([typeof(LoginBinder)]);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigureEndpoint(endpoint, catalog));

        Assert.Contains("login", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerBuildsRpcServiceCatalogFromDiscoveredBinderTypes()
    {
        var catalog = Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper
            .DiscoverRpcServiceCatalogForTesting([typeof(LoginBinder)]);

        Assert.True(catalog.TryGet("login", out var descriptor));
        Assert.Equal(typeof(LoginBinder), descriptor.BinderType);
    }

    private static readonly List<string> BoundServices = new();

    private static void ConfigureEndpoint(
        LakonaGameEndpointOptions endpoint,
        LakonaRpcServiceCatalog catalog)
    {
        var services = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddSingleton(catalog)
            .BuildServiceProvider();
        var builder = RpcServerHostBuilder.Create();
        var context = new LakonaGameServerRpcContext(
            endpoint.Transport,
            endpoint,
            builder,
            services,
            [],
            CancellationToken.None);
        var configurator = new LakonaEndpointRpcServerConfigurator(
            endpoint);

        configurator.Configure(context);
    }

    [LakonaRpcService("login")]
    private sealed class LoginBinder : LakonaRpcServiceBinder
    {
        public override void Bind(LakonaGameServerRpcContext context)
        {
            BoundServices.Add($"login:{context.Endpoint.Transport}");
        }
    }

    [LakonaRpcService("room")]
    private sealed class RoomBinder : LakonaRpcServiceBinder
    {
        public override void Bind(LakonaGameServerRpcContext context)
        {
            BoundServices.Add($"room:{context.Endpoint.Transport}");
        }
    }

    [LakonaRpcService("login")]
    private sealed class DuplicateLoginBinder : LakonaRpcServiceBinder
    {
        public override void Bind(LakonaGameServerRpcContext context)
        {
        }
    }

    private sealed class MissingAttributeBinder : LakonaRpcServiceBinder
    {
        public override void Bind(LakonaGameServerRpcContext context)
        {
        }
    }
}
