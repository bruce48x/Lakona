using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerRunnerTests
{
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

    private sealed class DisposalMarker : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
