using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class BootstrapperLifecycleTests
{
    [Fact]
    public async Task Stable_component_services_are_configured_before_host_build_and_owned_by_root()
    {
        await using var fixture = new Fixture();
        var host = await fixture.BuildAsync(FreePort());
        var resource = host.Services.GetRequiredService<RegisteredResource>();
        Assert.Equal("lifecycle-test", resource.NodeId);
        Assert.Equal(0, fixture.Probe.Starts);
        Assert.Equal(0, resource.Disposals);
        await ((IAsyncDisposable)host).DisposeAsync();
        Assert.Equal(1, resource.Disposals);
    }

    [Fact]
    public async Task Http_bind_failure_never_publishes_ready_and_runner_stops_started_components()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        await using var fixture = new Fixture();
        var host = await fixture.BuildAsync(((IPEndPoint)blocker.LocalEndpoint).Port);
        var readiness = host.Services.GetRequiredService<LakonaGameReadinessEvaluator>();
        var admission = host.Services.GetRequiredService<DistributedWorkAdmissionGate>();

        await Assert.ThrowsAnyAsync<IOException>(() => LakonaGameServerRunner.RunAsync(host));

        Assert.False(readiness.Evaluate().Succeeded);
        Assert.False(admission.IsOpen);
        Assert.Equal(1, fixture.Probe.Starts);
        Assert.Equal(1, fixture.Probe.Stops);
    }

    [Fact]
    public async Task Admission_waits_for_all_hosted_services_and_closes_before_their_stop()
    {
        await using var fixture = new Fixture();
        var host = await fixture.BuildAsync(FreePort(), services =>
            services.AddSingleton<IHostedService, ObserveAdmissionService>());
        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(host.Services.GetRequiredService<LakonaGameReadinessEvaluator>().Evaluate().Succeeded);
            Assert.True(fixture.Probe.ObservedStart);
            await host.StopAsync(TestContext.Current.CancellationToken);
            Assert.True(fixture.Probe.ObservedStop);
            Assert.Equal(1, fixture.Probe.Stops);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
            await ((IAsyncDisposable)host).DisposeAsync();
        }
    }

    [Fact]
    public async Task Later_start_failure_preserves_original_error_when_component_rollback_fails()
    {
        await using var fixture = new Fixture();
        fixture.Probe.FailStop = true;
        var host = await fixture.BuildAsync(FreePort(), services =>
            services.AddSingleton<IHostedService, FailingService>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LakonaGameServerRunner.RunAsync(host));

        Assert.Equal("later startup failed", exception.Message);
        Assert.Equal(1, fixture.Probe.Starts);
        Assert.Equal(1, fixture.Probe.Stops);
    }

    public sealed class LifecycleProbe
    {
        public int Starts, Stops;
        public bool FailStop, ObservedStart, ObservedStop;
    }

    public sealed class RegisteredResource(string nodeId) : IDisposable
    {
        public string NodeId { get; } = nodeId;
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    private sealed class ObserveAdmissionService(
        DistributedWorkAdmissionGate admission,
        LakonaGameReadinessEvaluator readiness,
        LifecycleProbe probe) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Assert.False(admission.IsOpen);
            Assert.False(readiness.Evaluate().Succeeded);
            probe.ObservedStart = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Assert.False(admission.IsOpen);
            Assert.False(readiness.Evaluate().Succeeded);
            probe.ObservedStop = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("later startup failed");
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "LakonaHostLifecycle", Guid.NewGuid().ToString("N"));
        private readonly Assembly application;
        public LifecycleProbe Probe { get; } = new();

        public Fixture()
        {
            _ = typeof(HotfixComponentAttribute).Assembly;
            Directory.CreateDirectory(Path.Combine(directory, "hotfix"));
            application = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Compile(
                "LifecycleApplication" + Guid.NewGuid().ToString("N"), "public sealed class ApplicationMarker {}")));
            File.WriteAllBytes(Path.Combine(directory, "hotfix", "Server.Hotfix.dll"), Compile(
                "LifecycleHotfix" + Guid.NewGuid().ToString("N"),
                """
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using Lakona.Game.Server;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Configuration;
                using Probe = Lakona.Game.Server.Tests.Hosting.BootstrapperLifecycleTests.LifecycleProbe;
                using Resource = Lakona.Game.Server.Tests.Hosting.BootstrapperLifecycleTests.RegisteredResource;
                [HotfixComponent, NodeRole("probe")]
                public sealed class ProbeComponent(Probe probe)
                {
                    [ComponentConfigureServices]
                    public static void Register(IServiceCollection services, IConfiguration configuration) =>
                        services.AddSingleton(_ => new Resource(configuration["Lakona:Node:Id"]));

                    [ComponentStart]
                    public ValueTask Start(CancellationToken cancellationToken)
                    {
                        probe.Starts++;
                        return default;
                    }
                    [ComponentStop]
                    public ValueTask Stop(CancellationToken cancellationToken)
                    {
                        probe.Stops++;
                        if (probe.FailStop) throw new InvalidOperationException("component rollback failed");
                        return default;
                    }
                }
                public sealed class Registration : IHotfixGeneratedServiceRegistration
                {
                    public void Register(IServiceCollection services) => services.AddSingleton<ProbeComponent>();
                }
                """));
        }

        public Task<IHost> BuildAsync(int httpPort, Action<IServiceCollection>? configure = null) =>
            LakonaGameServerBootstrapper.BuildAsyncForTesting([], builder =>
            {
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Lakona:Node:Id"] = "lifecycle-test",
                    ["Lakona:Node:Roles:0"] = "probe",
                    ["Lakona:Cluster:Endpoint"] = $"tcp://127.0.0.1:{FreePort()}",
                    ["Lakona:Health:Enabled"] = "true",
                    ["Lakona:Management:Http:Host"] = "127.0.0.1",
                    ["Lakona:Management:Http:Port"] = httpPort.ToString(),
                    ["Lakona:Hotfix:DebugWatcher"] = "On"
                }));
                builder.AddServices(services =>
                {
                    services.AddSingleton(Probe);
                    services.AddSingleton(new ClusterBuildTag("lifecycleTest"));
                    configure?.Invoke(services);
                });
            }, [application], directory);

        public ValueTask DisposeAsync()
        {
            Directory.Delete(directory, recursive: true);
            return default;
        }

        private static byte[] Compile(string name, string source)
        {
            var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(BootstrapperLifecycleTests).Assembly.Location)
                .Append(typeof(HotfixComponentAttribute).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)],
                paths.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            return stream.ToArray();
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
