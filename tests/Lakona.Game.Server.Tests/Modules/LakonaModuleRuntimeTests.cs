using Lakona.Game.Server.Health;
using Lakona.Game.Server.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests.Modules;

public sealed class LakonaModuleRuntimeTests
{
    [Fact]
    public void Discovery_is_deterministic_and_excludes_test_assemblies_by_default()
    {
        var assembly = typeof(LakonaModuleRuntimeTests).Assembly;

        Assert.Empty(LakonaModuleDiscovery.DiscoverTypes([assembly]));

        var discovered = LakonaModuleDiscovery.DiscoverTypes(
            [assembly],
            excludeTestAssemblies: false);
        var names = discovered
            .Select(static type => type.FullName)
            .ToArray();

        Assert.Contains(typeof(AlphaDiscoveredModule), discovered);
        Assert.Contains(typeof(ZuluDiscoveredModule), discovered);
        Assert.Equal(
            names.OrderBy(static name => name, StringComparer.Ordinal),
            names);
    }

    [Fact]
    public void Configure_registers_the_same_module_instance_and_the_final_service_graph()
    {
        AlphaDiscoveredModule.Reset();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var catalog = LakonaModuleDiscovery.ConfigureTypes(
            services,
            configuration,
            [typeof(AlphaDiscoveredModule)]);
        using var provider = services.BuildServiceProvider();

        var registration = Assert.Single(catalog.Modules);
        var concrete = provider.GetRequiredService<AlphaDiscoveredModule>();
        var abstraction = Assert.Single(provider.GetServices<ILakonaModule>());

        Assert.Same(registration.Instance, concrete);
        Assert.Same(concrete, abstraction);
        Assert.Same(
            provider.GetRequiredService<RegisteredByModule>(),
            provider.GetRequiredService<RegisteredByModule>());
        Assert.Equal(1, AlphaDiscoveredModule.ConfigureCount);
    }

    [Fact]
    public async Task Runtime_starts_sequentially_and_stops_in_reverse_order()
    {
        var events = new List<string>();
        var first = new RecordingModule("first", events);
        var second = new RecordingModule("second", events);
        await using var provider = CreateRuntimeProvider(first, second);
        var runtime = provider.GetRequiredService<LakonaModuleRuntime>();

        await runtime.StartAsync(TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken);
        await runtime.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["start:first", "start:second", "stop:second", "stop:first"],
            events);
    }

    [Fact]
    public async Task Startup_failure_rolls_back_successful_modules_and_preserves_original_failure()
    {
        var events = new List<string>();
        var first = new RecordingModule("first", events);
        var failure = new InvalidOperationException("redis refused");
        var second = new RecordingModule("second", events, startFailure: failure);
        await using var provider = CreateRuntimeProvider(first, second);
        var runtime = provider.GetRequiredService<LakonaModuleRuntime>();
        var readiness = provider.GetRequiredService<LakonaServerReadinessState>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        Assert.Equal(["start:first", "start:second", "stop:first"], events);
        Assert.Contains(
            readiness.Diagnostics,
            static diagnostic =>
                diagnostic.Code == LakonaServerReadinessState.FailedCode
                && diagnostic.Message.Contains("redis refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rollback_continues_after_a_module_stop_failure()
    {
        var events = new List<string>();
        var first = new RecordingModule(
            "first",
            events,
            stopFailure: new InvalidOperationException("first stop"));
        var second = new RecordingModule("second", events);
        var third = new RecordingModule(
            "third",
            events,
            startFailure: new InvalidOperationException("third start"));
        await using var provider = CreateRuntimeProvider(first, second, third);
        var runtime = provider.GetRequiredService<LakonaModuleRuntime>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("third start", exception.Message);
        Assert.Equal(
            [
                "start:first",
                "start:second",
                "start:third",
                "stop:second",
                "stop:first"
            ],
            events);
    }

    [Fact]
    public async Task Startup_cancellation_rolls_back_already_started_modules()
    {
        var events = new List<string>();
        var first = new RecordingModule("first", events);
        using var cancellation = new CancellationTokenSource();
        var second = new RecordingModule(
            "second",
            events,
            onStart: cancellation.Cancel);
        var third = new RecordingModule("third", events);
        await using var provider = CreateRuntimeProvider(first, second, third);
        var runtime = provider.GetRequiredService<LakonaModuleRuntime>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.StartAsync(cancellation.Token));

        Assert.Equal(
            ["start:first", "start:second", "stop:second", "stop:first"],
            events);
    }

    private static ServiceProvider CreateRuntimeProvider(
        params RecordingModule[] modules)
    {
        var registrations = modules
            .Select(module => new LakonaModuleRegistration(
                module.GetType(),
                module))
            .ToArray();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().Build());
        services.AddSingleton(new LakonaModuleCatalog(registrations));
        services.AddSingleton<LakonaServerReadinessState>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<LakonaModuleRuntime>>(
            NullLogger<LakonaModuleRuntime>.Instance);
        services.AddSingleton<LakonaModuleRuntime>();
        return services.BuildServiceProvider();
    }

    public sealed class AlphaDiscoveredModule : ILakonaModule
    {
        public static int ConfigureCount { get; private set; }

        public static void Reset()
        {
            ConfigureCount = 0;
        }

        public void ConfigureServices(
            IServiceCollection services,
            IConfiguration configuration)
        {
            ConfigureCount++;
            services.AddSingleton<RegisteredByModule>();
        }

        public Task StartAsync(
            ILakonaModuleContext context,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class ZuluDiscoveredModule : ILakonaModule
    {
        public void ConfigureServices(
            IServiceCollection services,
            IConfiguration configuration)
        {
        }

        public Task StartAsync(
            ILakonaModuleContext context,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class RegisteredByModule
    {
    }

    public sealed class RecordingModule : ILakonaModule
    {
        private readonly string name;
        private readonly List<string> events;
        private readonly Exception? startFailure;
        private readonly Exception? stopFailure;
        private readonly Action? onStart;

        public RecordingModule()
            : this("discovered", [])
        {
        }

        public RecordingModule(
            string name,
            List<string> events,
            Exception? startFailure = null,
            Exception? stopFailure = null,
            Action? onStart = null)
        {
            this.name = name;
            this.events = events;
            this.startFailure = startFailure;
            this.stopFailure = stopFailure;
            this.onStart = onStart;
        }

        public void ConfigureServices(
            IServiceCollection services,
            IConfiguration configuration)
        {
        }

        public Task StartAsync(
            ILakonaModuleContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"start:{name}");
            onStart?.Invoke();
            if (startFailure is not null)
            {
                throw startFailure;
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add($"stop:{name}");
            if (stopFailure is not null)
            {
                throw stopFailure;
            }

            return Task.CompletedTask;
        }
    }
}
