using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerRunnerTests
{
    [Theory]
    [InlineData(true, "startup")]
    [InlineData(false, "startup")]
    [InlineData(true, "shutdown")]
    [InlineData(false, "shutdown")]
    [InlineData(true, "none")]
    [InlineData(false, "none")]
    public async Task Runner_preserves_primary_failure_and_disposes_once(bool asynchronous, string failureStage)
    {
        var primary = new InvalidOperationException("primary failure");
        var rollbackFailure = new IOException("rollback failed");
        DisposalFailure resource = asynchronous ? new AsyncDisposalFailure() : new DisposalFailure();
        var service = default(FailingHostedService)!;
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_ => resource);
                services.AddSingleton<IHostedService>(provider => service = new FailingHostedService(
                    provider.GetRequiredService<IHostApplicationLifetime>(), primary,
                    rollbackFailure, failureStage));
            }).Build();
        _ = host.Services.GetRequiredService<DisposalFailure>();
        IHost runnerHost = asynchronous ? host : new SynchronousHost(host);

        var error = await Record.ExceptionAsync(() => LakonaGameServerRunner.RunAsync(runnerHost));

        Assert.Equal(1, service.Stops);
        Assert.Equal(1, resource.Disposals);
        Assert.Equal(asynchronous, resource.UsedAsyncDisposal);
        if (failureStage == "none")
        {
            Assert.Same(resource.Error, error);
        }
        else
        {
            Assert.Same(primary, error);
            Assert.Contains(failureStage == "startup" ? nameof(FailingHostedService.StartAsync) : nameof(FailingHostedService.StopAsync), error!.StackTrace);
            Assert.Same(resource.Error, error.Data["Lakona.HostDisposalFailure"]);
            if (failureStage == "startup")
                Assert.Same(rollbackFailure, error.Data["Lakona.StartupCleanupFailure"]);
        }
    }

    [Fact]
    public async Task Runner_uses_host_lifecycle_and_disposes_the_root_provider()
    {
        var marker = new DisposalMarker();
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_ => marker);
                services.AddHostedService<StopAfterStartHostedService>();
            })
            .Build();

        _ = host.Services.GetRequiredService<DisposalMarker>();

        await LakonaGameServerRunner.RunAsync(host);

        Assert.True(marker.IsDisposed);
    }

    private sealed class StopAfterStartHostedService(
        IHostApplicationLifetime lifetime) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private class DisposalFailure : IDisposable
    {
        public IOException Error { get; } = new("resource disposal failed");
        public int Disposals { get; protected set; }
        public bool UsedAsyncDisposal { get; protected set; }
        public void Dispose()
        {
            Disposals++;
            throw Error;
        }
    }

    private sealed class AsyncDisposalFailure : DisposalFailure, IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            UsedAsyncDisposal = true;
            Disposals++;
            throw Error;
        }
    }

    private sealed class FailingHostedService(
        IHostApplicationLifetime lifetime, Exception primary, Exception rollback, string stage) : IHostedService
    {
        public int Stops { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (stage == "startup") throw primary;
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            if (stage == "startup") throw rollback;
            if (stage == "shutdown") throw primary;
            return Task.CompletedTask;
        }
    }

    // Exercise the IHost-only disposal path while retaining the real Host lifecycle.
    private sealed class SynchronousHost(IHost inner) : IHost
    {
        public IServiceProvider Services => inner.Services;
        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
        public void Dispose() => inner.Dispose();
    }

    private sealed class DisposalMarker : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
