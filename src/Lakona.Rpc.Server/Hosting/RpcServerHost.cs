using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

public sealed class RpcServerHost
{
    private readonly Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> _acceptorFactory;
    private readonly ILogger _logger;
    private readonly RpcKeepAliveOptions _keepAlive;
    private readonly RpcServerLimits _limits;
    private readonly RpcServiceRegistry _registry;
    private readonly TransportSecurityConfig _security;
    private readonly IRpcSerializer _serializer;
    internal RpcServerHost(
        IRpcSerializer serializer,
        RpcServiceRegistry registry,
        TransportSecurityConfig security,
        RpcKeepAliveOptions keepAlive,
        Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> acceptorFactory,
        ILogger logger,
        RpcServerLimits limits)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        _acceptorFactory = acceptorFactory ?? throw new ArgumentNullException(nameof(acceptorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
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

        try
        {
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
            _logger.LogInformation("[{DisplayName}] disconnected.", connection.DisplayName);
        }
    }

    private ITransport WrapSecurity(ITransport transport)
    {
        if (!_security.IsEnabled)
            return transport;

        return new TransformingTransport(transport, _security);
    }
}
