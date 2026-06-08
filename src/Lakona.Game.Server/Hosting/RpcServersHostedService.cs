using Microsoft.Extensions.Hosting;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Hosting;

internal sealed class RpcServersHostedService : BackgroundService
{
    private readonly IReadOnlyList<IRpcServerConfigurator> _configurators;
    private readonly IServiceProvider _services;

    public RpcServersHostedService(
        IEnumerable<IRpcServerConfigurator> configurators,
        IServiceProvider services)
    {
        _configurators = configurators.ToArray();
        _services = services;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configurators.Count == 0)
        {
            return Task.CompletedTask;
        }

        var tasks = new Task[_configurators.Count];
        for (var i = 0; i < _configurators.Count; i++)
        {
            tasks[i] = RunServerAsync(_configurators[i], stoppingToken);
        }

        return Task.WhenAll(tasks);
    }

    private async Task RunServerAsync(IRpcServerConfigurator configurator, CancellationToken stoppingToken)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var builder = RpcServerHostBuilder.Create()
            .UseCommandLine(args);
        configurator.Configure(new LakonaGameServerRpcContext(
            configurator.Name,
            builder,
            _services,
            args,
            stoppingToken));

        await builder.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
