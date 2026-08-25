using System.Reflection;
using Lakona.Game.Server.Hotfix.BuildTag;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaServerStartupHostedServiceTests
{
    private static readonly string ExpectedBuildTag =
        HotfixBuildTag.Get(Assembly.GetEntryAssembly() ?? typeof(LakonaServerStartupHostedService).Assembly);

    private static readonly string ExpectedMessage =
        $"Lakona server started successfully. NodeId=node-a. LakonaBuildTag={ExpectedBuildTag}.";

    [Fact]
    public async Task Success_log_is_written_after_all_hosted_services_start()
    {
        var events = new List<string>();
        using var loggerProvider = new RecordingLoggerProvider(events);
        using var host = CreateHost(
            loggerProvider,
            services => services.AddSingleton<IHostedService>(
                new RecordingHostedService(events)));

        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(["component-started", "startup-log"], events);
            var entry = Assert.Single(
                loggerProvider.Entries,
                entry => entry.Message == ExpectedMessage);
            Assert.Equal(LogLevel.Information, entry.Level);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Success_log_contains_node_id_and_build_tag_properties()
    {
        using var loggerProvider = new RecordingLoggerProvider([]);
        using var host = CreateHost(loggerProvider);

        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var entry = Assert.Single(
                loggerProvider.Entries,
                entry => entry.Message == ExpectedMessage);
            Assert.Equal("node-a", entry.State["NodeId"]);
            Assert.Equal(ExpectedBuildTag, entry.State["LakonaBuildTag"]);
            Assert.DoesNotContain("StartupActors", entry.State.Keys);
            Assert.DoesNotContain("Listeners", entry.State.Keys);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Hosted_service_start_failure_suppresses_success_log()
    {
        using var loggerProvider = new RecordingLoggerProvider([]);
        using var host = CreateHost(
            loggerProvider,
            services => services.AddSingleton<IHostedService, ThrowingHostedService>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("startup failed", exception.Message);
        Assert.DoesNotContain(
            loggerProvider.Entries,
            entry => entry.Message == ExpectedMessage);
    }

    [Fact]
    public void AddLakonaGameServer_registers_one_node_lifecycle_bridge()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServer();
        services.AddLakonaGameServer();

        var descriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(LakonaNodeHostedService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(
            7,
            services.Count(descriptor =>
                descriptor.ServiceType == typeof(ILakonaNodeLifecycleParticipant)));
    }

    [Fact]
    public async Task Stopping_closes_and_drains_distributed_admission_before_service_stop()
    {
        var observations = new List<bool>();
        using var loggerProvider = new RecordingLoggerProvider([]);
        using var host = CreateHost(
            loggerProvider,
            services => services.AddSingleton<IHostedService>(provider =>
                new AdmissionObservingHostedService(
                    provider.GetRequiredService<DistributedWorkAdmissionGate>(),
                    observations)));

        await host.StartAsync(TestContext.Current.CancellationToken);
        var gate = host.Services.GetRequiredService<DistributedWorkAdmissionGate>();
        Assert.True(gate.TryEnter(out var admission));

        var stop = host.StopAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        Assert.False(gate.IsOpen);
        Assert.False(stop.IsCompleted);

        gate.Exit(admission);
        await stop;

        Assert.Equal([false], observations);
    }

    private static IHost CreateHost(
        RecordingLoggerProvider loggerProvider,
        Action<IServiceCollection>? configureServices = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging
                .ClearProviders()
                .AddProvider(loggerProvider))
            .ConfigureServices(services =>
            {
                services.AddSingleton(new LakonaGameRuntimeOptions
                {
                    Node = new LakonaGameNodeOptions { Id = "node-a" }
                });
                services.AddSingleton<LakonaServerReadinessState>();
                services.AddSingleton<DistributedWorkAdmissionGate>();
                configureServices?.Invoke(services);
                services.AddSingleton<IHostedService, LakonaServerStartupHostedService>();
            })
            .Build();
    }

    private sealed class RecordingHostedService(List<string> events) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("component-started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("startup failed");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class AdmissionObservingHostedService(
        DistributedWorkAdmissionGate gate,
        List<bool> observations) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            observations.Add(gate.IsOpen);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLoggerProvider(List<string> events) : ILoggerProvider
    {
        private readonly object _gate = new();

        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(this, categoryName, events);
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(
            RecordingLoggerProvider owner,
            string category,
            List<string> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(static value => value.Key, static value => value.Value)
                    : new Dictionary<string, object?>();
                lock (owner._gate)
                {
                    owner.Entries.Add(new LogEntry(
                        category,
                        logLevel,
                        message,
                        exception,
                        properties));
                    if (message == ExpectedMessage)
                    {
                        events.Add("startup-log");
                    }
                }
            }
        }
    }

    private sealed record LogEntry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> State);
}
