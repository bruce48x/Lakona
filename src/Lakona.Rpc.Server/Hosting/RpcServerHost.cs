using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

public sealed class RpcServerHost
{
    private readonly Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> _acceptorFactory;
    private readonly ILogger _logger;
    private readonly RpcKeepAliveOptions _keepAlive;
    private readonly RpcServerLimits _limits;
    private readonly RpcServiceRegistry _registry;
    private readonly IReadOnlyList<IRpcSessionLifecycleObserver> _sessionLifecycleObservers;
    private readonly TransportSecurityConfig _security;
    private readonly IRpcSerializer _serializer;
    internal RpcServerHost(
        IRpcSerializer serializer,
        RpcServiceRegistry registry,
        TransportSecurityConfig security,
        RpcKeepAliveOptions keepAlive,
        Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> acceptorFactory,
        ILogger logger,
        RpcServerLimits limits,
        IReadOnlyList<IRpcSessionLifecycleObserver>? sessionLifecycleObservers = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        _acceptorFactory = acceptorFactory ?? throw new ArgumentNullException(nameof(acceptorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _sessionLifecycleObservers = sessionLifecycleObservers ?? Array.Empty<IRpcSessionLifecycleObserver>();
    }

    public async ValueTask RunAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ConsoleCancelEventHandler? cancelHandler = null;
        var connectionTasks = new TrackedTaskCollection();

        cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var baseAcceptor = await _acceptorFactory(cts.Token).ConfigureAwait(false);
            // Ownership of baseAcceptor is transferred to BoundedConnectionAcceptor here.
            // BoundedConnectionAcceptor.DisposeAsync() calls _inner.DisposeAsync() internally,
            // so baseAcceptor must NOT be held in an "await using" — doing so causes a double-Dispose.
            await using var acceptor = new BoundedConnectionAcceptor(
                baseAcceptor,
                _limits.MaxPendingAcceptedConnections,
                _logger,
                cts.Token);
            _logger.LogInformation("RPC server listening on {ListenAddress}. Press Ctrl+C to stop.", baseAcceptor.ListenAddress);

            while (!cts.IsCancellationRequested)
            {
                RpcAcceptedConnection connection;
                try
                {
                    connection = await acceptor.AcceptAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                _logger.LogInformation("[{DisplayName}] accepted.", connection.DisplayName);

                var connectionTask = RunConnectionAsync(connection, cts.Token);
                connectionTasks.Track(connectionTask);
            }

            cts.Cancel();
            await connectionTasks.WaitAsync().ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            _logger.LogInformation("Server stopped.");
        }
    }

    private async Task RunConnectionAsync(RpcAcceptedConnection connection, CancellationToken hostCt)
    {
        var transport = WrapSecurity(connection.Transport);
        await using var session = new RpcSession(
            transport,
            _serializer,
            _registry,
            connection.DisplayName,
            ownsTransport: true,
            keepAlive: _keepAlive,
            logger: _logger,
            limits: _limits);
        var lifecycleContext = new RpcSessionLifecycleContext(session.ContextId, connection.DisplayName);
        Exception? disconnectError = null;
        session.Disconnected += ex => disconnectError = ex;

        try
        {
            await NotifySessionStartedAsync(lifecycleContext, hostCt).ConfigureAwait(false);
            await session.RunAsync(hostCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{DisplayName}] Error.", connection.DisplayName);
        }
        finally
        {
            await NotifySessionDisconnectedAsync(lifecycleContext, disconnectError, hostCt).ConfigureAwait(false);
            _logger.LogInformation("[{DisplayName}] disconnected.", connection.DisplayName);
        }
    }

    private async ValueTask NotifySessionStartedAsync(
        RpcSessionLifecycleContext context,
        CancellationToken cancellationToken)
    {
        foreach (var observer in _sessionLifecycleObservers)
        {
            try
            {
                await observer.OnSessionStartedAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{DisplayName}] RPC session lifecycle start observer failed.", context.DisplayName);
            }
        }
    }

    private async ValueTask NotifySessionDisconnectedAsync(
        RpcSessionLifecycleContext context,
        Exception? error,
        CancellationToken cancellationToken)
    {
        foreach (var observer in _sessionLifecycleObservers)
        {
            try
            {
                await observer.OnSessionDisconnectedAsync(context, error, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{DisplayName}] RPC session lifecycle disconnect observer failed.", context.DisplayName);
            }
        }
    }

    private ITransport WrapSecurity(ITransport transport)
    {
        if (!_security.IsEnabled)
            return transport;

        return new TransformingTransport(transport, _security);
    }
}
