using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix;
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
        Assert.Equal(1, fixture.Probe.Unloads);
        Assert.NotNull(fixture.Probe.PreparedContext);
        for (var attempt = 0; attempt < 30 && fixture.Probe.PreparedContext.IsAlive; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.False(fixture.Probe.PreparedContext.IsAlive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Root_factory_failure_releases_prior_async_resource_and_prepared_context(bool failCleanup)
    {
        await using var fixture = new Fixture();
        fixture.Probe.FailFactory = true;
        fixture.Probe.FailResourceDispose = failCleanup;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => fixture.BuildAsync(FreePort()));
        Assert.Contains("root factory failed", error.ToString());
        if (failCleanup)
        {
            var aggregate = Assert.IsType<AggregateException>(error);
            Assert.Contains(aggregate.InnerExceptions, item => item.Message == "root factory failed");
            Assert.Contains("resource cleanup failed", aggregate.ToString());
        }
        else Assert.IsType<InvalidOperationException>(error);
        Assert.NotNull(fixture.Probe.Resource);
        Assert.Equal(1, fixture.Probe.Resource.Disposals);
        Assert.Equal(1, fixture.Probe.Unloads);
        Assert.Equal(0, fixture.Probe.Starts);
        Assert.Equal(0, fixture.Probe.Stops);
    }

    [Fact]
    public async Task Async_component_start_completes_before_publication_and_readiness()
    {
        await using var fixture = new Fixture();
        fixture.Probe.BlockStart = true;
        var host = await fixture.BuildAsync(FreePort());
        var starting = host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await fixture.Probe.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(starting.IsCompleted);
            Assert.Null(host.Services.GetRequiredService<IHotfixManager>().Current.LoadedAtUtc);
            Assert.False(host.Services.GetRequiredService<LakonaGameReadinessEvaluator>().Evaluate().Succeeded);
            Assert.False(host.Services.GetRequiredService<DistributedWorkAdmissionGate>().IsOpen);
            fixture.Probe.StartRelease.TrySetResult();
            await starting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotNull(host.Services.GetRequiredService<IHotfixManager>().Current.LoadedAtUtc);
            Assert.True(host.Services.GetRequiredService<LakonaGameReadinessEvaluator>().Evaluate().Succeeded);
        }
        finally
        {
            fixture.Probe.StartRelease.TrySetResult();
            try { await starting; }
            finally { await StopAndDisposeAsync(host); }
        }
    }

    [Fact]
    public async Task Async_component_stop_completes_before_generation_and_root_resource_disposal()
    {
        await using var fixture = new Fixture();
        var host = await fixture.BuildAsync(FreePort());
        await host.StartAsync(TestContext.Current.CancellationToken);
        fixture.Probe.BlockStop = true;
        var stopping = host.StopAsync(TestContext.Current.CancellationToken);
        try
        {
            await fixture.Probe.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(stopping.IsCompleted);
            Assert.Equal(0, fixture.Probe.GenerationDisposals);
            Assert.Equal(0, fixture.Probe.Resource!.Disposals);
            fixture.Probe.StopRelease.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(1, fixture.Probe.GenerationDisposals);
            Assert.Equal(0, fixture.Probe.Resource.Disposals);
        }
        finally
        {
            fixture.Probe.StopRelease.TrySetResult();
            try { await stopping; }
            finally { await ((IAsyncDisposable)host).DisposeAsync(); }
        }
        Assert.Equal(1, fixture.Probe.Resource.Disposals);
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
        public bool FailFactory, FailResourceDispose, BlockStart, BlockStop;
        public int Unloads, GenerationDisposals;
        public RegisteredResource? Resource;
        public WeakReference? PreparedContext;
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class RegisteredResource(string nodeId) : IAsyncDisposable
    {
        public string NodeId { get; } = nodeId;
        public int Disposals;
        public bool FailDispose;
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Disposals++;
            if (FailDispose) throw new InvalidOperationException("resource cleanup failed");
        }
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
                using System.Runtime.Loader;
                using Lakona.Game.Server;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Lakona.Game.Server.Hotfix;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Configuration;
                using Probe = Lakona.Game.Server.Tests.Hosting.BootstrapperLifecycleTests.LifecycleProbe;
                using Resource = Lakona.Game.Server.Tests.Hosting.BootstrapperLifecycleTests.RegisteredResource;
                [HotfixComponent, NodeRole("probe")]
                public sealed class ProbeComponent(Probe probe) : IAsyncDisposable
                {
                    [ComponentConfigureServices]
                    public static void Register(IServiceCollection services, IConfiguration configuration)
                    {
                        services.AddSingleton(provider =>
                        {
                            var probe = provider.GetRequiredService<Probe>();
                            var context = AssemblyLoadContext.GetLoadContext(typeof(ProbeComponent).Assembly);
                            probe.PreparedContext = new WeakReference(context);
                            context.Unloading += _ => probe.Unloads++;
                            return probe.Resource = new Resource(configuration["Lakona:Node:Id"]) { FailDispose = probe.FailResourceDispose };
                        });
                        services.AddSingleton<object>(provider => provider.GetRequiredService<Probe>().FailFactory
                            ? throw new InvalidOperationException("root factory failed") : new object());
                    }

                    [ComponentStart]
                    public async ValueTask Start(CancellationToken cancellationToken)
                    {
                        probe.Starts++;
                        probe.StartEntered.TrySetResult();
                        if (probe.BlockStart) await probe.StartRelease.Task.WaitAsync(cancellationToken);
                    }
                    [ComponentStop]
                    public async ValueTask Stop(CancellationToken cancellationToken)
                    {
                        probe.Stops++;
                        probe.StopEntered.TrySetResult();
                        if (probe.BlockStop) await probe.StopRelease.Task.WaitAsync(cancellationToken);
                        if (probe.FailStop) throw new InvalidOperationException("component rollback failed");
                    }
                    public ValueTask DisposeAsync() { probe.GenerationDisposals++; return default; }
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

    private static async Task StopAndDisposeAsync(IHost host)
    {
        try { await host.StopAsync(CancellationToken.None); }
        finally { await ((IAsyncDisposable)host).DisposeAsync(); }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
