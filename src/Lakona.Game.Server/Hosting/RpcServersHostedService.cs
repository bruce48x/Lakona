using Microsoft.Extensions.Hosting;
using Lakona.Rpc.Server;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Hosting;

internal sealed class RpcServersHostedService : BackgroundService
{
    private readonly IReadOnlyList<IRpcServerConfigurator> _configurators;
    private readonly IServiceProvider _services;
    private readonly TaskCompletionSource _listening = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public RpcServersHostedService(
        IEnumerable<IRpcServerConfigurator> configurators,
        IServiceProvider services)
    {
        _configurators = configurators.ToArray();
        _services = services;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        await _listening.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configurators.Count == 0)
        {
            _listening.TrySetResult();
            return;
        }

        var tasks = new Task[_configurators.Count];
        var remaining = _configurators.Count;
        for (var i = 0; i < _configurators.Count; i++)
        {
            tasks[i] = RunServerAsync(
                _configurators[i],
                () =>
                {
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        _listening.TrySetResult();
                    }
                },
                stoppingToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RunServerAsync(
        IRpcServerConfigurator configurator,
        Action onListening,
        CancellationToken stoppingToken)
    {
        try
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var runtimeOptions = (LakonaGameRuntimeOptions?)_services.GetService(typeof(LakonaGameRuntimeOptions));
            var endpoint = ResolveEndpoint(runtimeOptions, configurator.Transport);
            var builder = RpcServerHostBuilder.Create()
                .UseCommandLine(args);
            configurator.Configure(new LakonaGameServerRpcContext(
                configurator.Transport,
                endpoint,
                builder,
                _services,
                args,
                stoppingToken));

            await builder.RunAsync(
                stoppingToken,
                _ => onListening()).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _listening.TrySetCanceled(stoppingToken);
            throw;
        }
        catch (Exception exception)
        {
            _listening.TrySetException(exception);
            throw;
        }
    }

    private static LakonaGameEndpointOptions ResolveEndpoint(
        LakonaGameRuntimeOptions? runtimeOptions,
        string transport)
    {
        return runtimeOptions?.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Transport, transport, StringComparison.OrdinalIgnoreCase))
            ?? new LakonaGameEndpointOptions { Transport = transport };
    }
}
