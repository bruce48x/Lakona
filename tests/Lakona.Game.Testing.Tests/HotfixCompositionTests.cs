using Lakona.Game.Server.Hotfix;
using Lakona.Game.Testing.Fixtures.App;
using Lakona.Game.Testing.Fixtures.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class HotfixCompositionTests
{
    [Fact]
    public async Task Failed_activation_releases_partially_constructed_generation_asynchronously()
    {
        CompositionProbeState? state = null;
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .ConfigureNodes(node =>
            {
                node.UseHotfixAssembly(typeof(HttpCompositionProbe).Assembly);
                node.ConfigureServices((services, _) =>
                    services.AddSingleton<Action<CompositionProbeState>>(value => state = value));
                node.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Lakona:Http:Listeners:0:Id"] = "test",
                        ["Lakona:Http:Listeners:0:Host"] = "127.0.0.1",
                        ["Lakona:Http:Listeners:0:Port"] = "21000",
                        ["Lakona:Http:Listeners:0:Services:0"] = "disabled"
                    }));
            })
            .Build();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            cluster.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Disabled HTTP module was activated", error.ToString());
        Assert.NotNull(state);
        Assert.Equal(1, state.DisposeCount);
    }

    [Fact]
    public async Task Only_enabled_http_services_are_activated_and_exposed()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .ConfigureNodes(node =>
            {
                node.UseHotfixAssembly(typeof(HttpCompositionProbe).Assembly);
                node.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Lakona:Http:Listeners:0:Id"] = "test",
                        ["Lakona:Http:Listeners:0:Host"] = "127.0.0.1",
                        ["Lakona:Http:Listeners:0:Port"] = "21000",
                        ["Lakona:Http:Listeners:0:Services:0"] = "probe"
                    }));
            })
            .Build();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        using var lease = cluster.Node("data-1").Services
            .GetRequiredService<IHotfixRuntimeAccessor>().AcquireCurrent();
        Assert.Equal("probe", Assert.Single(lease.Snapshot.HttpEndpoints).Service);
    }

    [Fact]
    public async Task Components_use_node_owned_stable_state_and_complete_lifecycle_before_disposal()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("data-2", "data")
            .ConfigureNodes(node => node.UseHotfixAssembly(typeof(CompositionProbe).Assembly))
            .Build();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var root = cluster.Node("data-1").Services;
        var runtime = root.GetRequiredService<IHotfixRuntimeAccessor>().Current;
        var probe = Assert.Single(runtime.Services.GetServices<CompositionProbe>());
        Assert.Equal("generated", probe.Origin);
        var state = root.GetRequiredService<CompositionProbeState>();
        var registration = root.GetRequiredService<CompositionRegistrationProbe>();
        Assert.Equal("data-1", registration.NodeId);
        Assert.Same(registration, runtime.Services.GetRequiredService<CompositionRegistrationProbe>());
        Assert.NotSame(registration, cluster.Node("data-2").Services.GetRequiredService<CompositionRegistrationProbe>());
        Assert.Same(state, runtime.Services.GetRequiredService<CompositionProbeState>());
        Assert.Equal(1, state.Starts);
        Assert.Equal(0, state.Stops);
        Assert.NotSame(state, cluster.Node("data-2").Services.GetRequiredService<CompositionProbeState>());
        Assert.Null(root.GetService<CompositionProbe>());
        Assert.Same(root.GetRequiredService<Lakona.Game.Server.ILakonaGameServer>(),
            runtime.Services.GetRequiredService<Lakona.Game.Server.ILakonaGameServer>());
        Assert.NotSame(probe, cluster.Node("data-2").Services
            .GetRequiredService<IHotfixRuntimeAccessor>().Current.Services.GetRequiredService<CompositionProbe>());

        await cluster.DisposeAsync();
        Assert.Equal(1, probe.DisposeCount);
        Assert.Equal(1, state.Stops);
        Assert.Equal(1, state.StateDisposals);
        Assert.Equal(1, registration.Disposals);
    }
}
