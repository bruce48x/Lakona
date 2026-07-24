using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerRunnerTests
{
    [Fact]
    public async Task Runner_preserves_module_hotfix_host_and_shutdown_order()
    {
        var events = new List<string>();
        var readiness = new LakonaServerReadinessState();
        var module = new RecordingModule(events);
        var host = CreateHost(
            module,
            readiness,
            services => services.AddHostedService<StopAfterStartHostedService>());

        await LakonaGameServerRunner.RunAsync(
            host,
            _ =>
            {
                events.Add("hotfix-load");
                return Task.CompletedTask;
            });

        Assert.Equal(
            [
                "module-start",
                "hotfix-load",
                "host-start",
                "host-stop",
                "module-stop"
            ],
            events);
        Assert.Contains(
            readiness.Diagnostics,
            static diagnostic =>
                diagnostic.Code == LakonaServerReadinessState.StoppingCode);
    }

    [Fact]
    public async Task Initial_hotfix_failure_stops_modules_without_starting_the_host()
    {
        var events = new List<string>();
        var failure = new InvalidOperationException("hotfix failed");
        var host = CreateHost(
            new RecordingModule(events),
            new LakonaServerReadinessState(),
            services => services.AddHostedService<UnexpectedHostedService>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LakonaGameServerRunner.RunAsync(
                host,
                _ =>
                {
                    events.Add("hotfix-load");
                    throw failure;
                }));

        Assert.Same(failure, exception);
        Assert.Equal(
            ["module-start", "hotfix-load", "module-stop"],
            events);
    }

    [Fact]
    public async Task Framework_failure_wins_over_module_cleanup_failure()
    {
        var events = new List<string>();
        var frameworkFailure = new InvalidOperationException("framework failed");
        var host = CreateHost(
            new RecordingModule(
                events,
                stopFailure: new InvalidOperationException("module stop failed")),
            new LakonaServerReadinessState(),
            services => services.AddSingleton<IHostedService>(
                new ThrowingHostedService(events, frameworkFailure)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LakonaGameServerRunner.RunAsync(
                host,
                _ => Task.CompletedTask));

        Assert.Same(frameworkFailure, exception);
        Assert.Equal(
            ["module-start", "host-start", "host-stop", "module-stop"],
            events);
    }

    private static IHost CreateHost(
        RecordingModule module,
        LakonaServerReadinessState readiness,
        Action<IServiceCollection> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(static logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton(new LakonaModuleCatalog(
                [
                    new LakonaModuleRegistration(
                        module.GetType(),
                        module)
                ]));
                services.AddSingleton(readiness);
                services.AddSingleton<LakonaModuleRuntime>();
                services.AddSingleton(module.Events);
                configure(services);
            })
            .Build();
    }

    public sealed class RecordingModule(
        List<string> events,
        Exception? stopFailure = null) : ILakonaModule
    {
        public RecordingModule()
            : this([])
        {
        }

        internal List<string> Events => events;

        public void ConfigureServices(
            IServiceCollection services,
            IConfiguration configuration)
        {
        }

        public Task StartAsync(
            ILakonaModuleContext context,
            CancellationToken cancellationToken)
        {
            events.Add("module-start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("module-stop");
            if (stopFailure is not null)
            {
                throw stopFailure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StopAfterStartHostedService(
        IHostApplicationLifetime lifetime,
        List<string> events) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("host-start");
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("host-stop");
            return Task.CompletedTask;
        }
    }

    private sealed class UnexpectedHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "The host must not start after initial Hotfix failure.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHostedService(
        List<string> events,
        Exception failure) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("host-start");
            throw failure;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("host-stop");
            return Task.CompletedTask;
        }
    }
}
