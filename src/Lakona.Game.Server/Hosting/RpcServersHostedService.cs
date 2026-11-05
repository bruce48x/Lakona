using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Server;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;

namespace Lakona.Game.Server.Hosting;

internal sealed class RpcServersHostedService(
    IEnumerable<IRpcServerConfigurator> configurators,
    IServiceProvider services,
    DistributedWorkAdmissionGate admission,
    LakonaServerReadinessState readiness,
    ILogger<RpcServersHostedService> logger,
    IHostApplicationLifetime? lifetime = null) : BackgroundService
{
    private readonly IReadOnlyList<IRpcServerConfigurator> _configurators = configurators.ToArray();
    private readonly TaskCompletionSource _listening = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private bool ApplicationStopping => lifetime?.ApplicationStopping.IsCancellationRequested == true;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
            await _listening.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await base.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
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
        string? listenAddress = null;
        try
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var runtimeOptions = (LakonaGameRuntimeOptions?)services.GetService(typeof(LakonaGameRuntimeOptions));
            var endpoint = ResolveEndpoint(runtimeOptions, configurator.Transport);
            var builder = RpcServerHostBuilder.Create()
                .UseCommandLine(args)
                .UseServerLifecycleObserver(new ListenerObserver(
                    context =>
                    {
                        listenAddress = context.ListenAddress;
                        onListening();
                    },
                    (context, error) => ReportFailure(context.ListenAddress, error)));
            configurator.Configure(new LakonaGameServerRpcContext(
                configurator.Transport,
                endpoint,
                builder,
                services,
                args,
                stoppingToken));

            await builder.RunAsync(stoppingToken).ConfigureAwait(false);
            if (!stoppingToken.IsCancellationRequested && !ApplicationStopping)
            {
                ReportFailure(listenAddress, new InvalidOperationException("The RPC listener exited unexpectedly."));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _listening.TrySetCanceled(stoppingToken);
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(listenAddress, exception);
            throw;
        }

        void ReportFailure(string? address, Exception exception)
        {
            // Until every endpoint is listening, StartAsync owns rollback.
            if (_listening.TrySetException(exception) || !_listening.Task.IsCompletedSuccessfully)
                return;

            if (stoppingToken.IsCancellationRequested || ApplicationStopping)
                return;

            if (!readiness.TryMarkListenerFailed(configurator.Transport, address ?? "(unknown)", exception))
                return;

            admission.Fence();
            try
            {
                logger.LogCritical(exception, "RPC listener {Transport} on {ListenAddress} failed. Stopping the node.",
                    configurator.Transport, address);
            }
            finally
            {
                lifetime?.StopApplication();
            }
        }
    }

    private sealed class ListenerObserver(
        Action<RpcServerListeningContext> onListening,
        Action<RpcServerListeningContext, Exception> onFailed) : IRpcServerLifecycleObserver
    {
        public ValueTask OnListeningAsync(
            RpcServerListeningContext context,
            CancellationToken cancellationToken = default)
        {
            onListening(context);
            return default;
        }

        public void OnListenerFailed(RpcServerListeningContext context, Exception exception) =>
            onFailed(context, exception);
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
