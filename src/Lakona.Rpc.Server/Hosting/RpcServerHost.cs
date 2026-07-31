using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

public sealed class RpcServerHost
{
    private readonly Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> _acceptorFactory;
    private readonly ILogger _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly RpcKeepAliveOptions _keepAlive;
    private readonly RpcServerLimits _limits;
    private readonly RpcServiceRegistry _registry;
    private readonly IReadOnlyList<IRpcSessionAdmissionGate> _sessionAdmissionGates;
    private readonly IReadOnlyList<IRpcSessionRequestGate> _requestGates;
    private readonly IReadOnlyList<IRpcSessionLifecycleObserver> _sessionLifecycleObservers;
    private readonly IReadOnlyList<IRpcServerLifecycleObserver> _serverLifecycleObservers;
    private readonly TransportSecurityConfig _security;
    private readonly IRpcSerializer _serializer;
    private int _activeConnections;
    internal RpcServerHost(
        IRpcSerializer serializer,
        RpcServiceRegistry registry,
        TransportSecurityConfig security,
        RpcKeepAliveOptions keepAlive,
        Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> acceptorFactory,
        ILogger logger,
        RpcServerLimits limits,
        IReadOnlyList<IRpcSessionAdmissionGate>? sessionAdmissionGates = null,
        IReadOnlyList<IRpcSessionRequestGate>? requestGates = null,
        IReadOnlyList<IRpcSessionLifecycleObserver>? sessionLifecycleObservers = null,
        IReadOnlyList<IRpcServerLifecycleObserver>? serverLifecycleObservers = null,
        ILoggerFactory? loggerFactory = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        _acceptorFactory = acceptorFactory ?? throw new ArgumentNullException(nameof(acceptorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory;
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _sessionAdmissionGates = sessionAdmissionGates ?? Array.Empty<IRpcSessionAdmissionGate>();
        _requestGates = requestGates ?? Array.Empty<IRpcSessionRequestGate>();
        _sessionLifecycleObservers = sessionLifecycleObservers ?? Array.Empty<IRpcSessionLifecycleObserver>();
        _serverLifecycleObservers = serverLifecycleObservers ?? Array.Empty<IRpcServerLifecycleObserver>();
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
            _logger.LogInformation(
                "RPC server listening on {ListenAddress}.",
                baseAcceptor.ListenAddress);
            await NotifyListeningAsync(baseAcceptor.ListenAddress, cts.Token).ConfigureAwait(false);

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

                if (Interlocked.Increment(ref _activeConnections) > _limits.MaxActiveConnections)
                {
                    Interlocked.Decrement(ref _activeConnections);
                    _logger.LogWarning(
                        "[{DisplayName}] Rejected because the active RPC connection limit is full.",
                        connection.DisplayName);
                    await DisposeRejectedConnectionAsync(connection).ConfigureAwait(false);
                    continue;
                }

                var connectionTask = RunAdmittedConnectionAsync(connection, cts.Token);
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

    private async Task RunAdmittedConnectionAsync(
        RpcAcceptedConnection connection,
        CancellationToken hostCt)
    {
        try
        {
            await RunConnectionAsync(connection, hostCt).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    private async ValueTask DisposeRejectedConnectionAsync(RpcAcceptedConnection connection)
    {
        try
        {
            await connection.Transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "[{DisplayName}] Failed to dispose a rejected RPC connection.",
                connection.DisplayName);
        }
    }

    private async ValueTask NotifyListeningAsync(
        string listenAddress,
        CancellationToken cancellationToken)
    {
        if (_serverLifecycleObservers.Count == 0)
            return;

        var context = new RpcServerListeningContext(listenAddress);
        foreach (var observer in _serverLifecycleObservers)
        {
            await observer.OnListeningAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunConnectionAsync(RpcAcceptedConnection connection, CancellationToken hostCt)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var admissionLeases = new List<IAsyncDisposable>(_sessionAdmissionGates.Count);
        var lifetimeTokens = new List<CancellationToken>(_sessionAdmissionGates.Count + 1) { hostCt };

        try
        {
            var admissionContext = new RpcSessionAdmissionContext(connectionId, connection.DisplayName);
            foreach (var gate in _sessionAdmissionGates)
            {
                RpcSessionAdmissionResult result;
                try
                {
                    result = await gate.EvaluateAsync(admissionContext, hostCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[{DisplayName}] RPC session admission gate failed.",
                        connection.DisplayName);
                    await DisposeRejectedConnectionAsync(connection).ConfigureAwait(false);
                    return;
                }

                if (!result.IsAllowed)
                {
                    _logger.LogWarning(
                        "[{DisplayName}] RPC session admission rejected: {Reason}.",
                        connection.DisplayName,
                        result.RejectionReason);
                    await DisposeRejectedConnectionAsync(connection).ConfigureAwait(false);
                    return;
                }

                if (result.Lease is not null)
                    admissionLeases.Add(result.Lease);
                if (result.SessionCancellation.CanBeCanceled)
                    lifetimeTokens.Add(result.SessionCancellation);
            }

            using var sessionCts = lifetimeTokens.Count == 1
                ? null
                : CancellationTokenSource.CreateLinkedTokenSource(lifetimeTokens.ToArray());
            var sessionCt = sessionCts?.Token ?? hostCt;
            var transport = WrapSecurity(connection.Transport);
            await using var session = new RpcSession(
                transport,
                _serializer,
                _registry,
                connectionId,
                ownsTransport: true,
                keepAlive: _keepAlive,
                logger: _logger,
                requestLogger: _loggerFactory?.CreateLogger(RpcServerRequestLogging.Category),
                limits: _limits,
                requestGates: _requestGates,
                remoteEndPoint: connection.RemoteEndPoint);
            var lifecycleContext = new RpcSessionLifecycleContext(connectionId, connection.DisplayName);
            Exception? disconnectError = null;
            session.Disconnected += ex => disconnectError = ex;

            try
            {
                await NotifySessionStartedAsync(lifecycleContext, sessionCt).ConfigureAwait(false);
                await session.RunAsync(sessionCt).ConfigureAwait(false);
            }
            finally
            {
                await NotifySessionDisconnectedAsync(lifecycleContext, disconnectError, CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.LogInformation("[{DisplayName}] disconnected.", connection.DisplayName);
            }
        }
        catch (OperationCanceledException) when (hostCt.IsCancellationRequested || lifetimeTokens.Any(static token => token.IsCancellationRequested))
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{DisplayName}] Error.", connection.DisplayName);
        }
        finally
        {
            for (var index = admissionLeases.Count - 1; index >= 0; index--)
            {
                try
                {
                    await admissionLeases[index].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[{DisplayName}] Failed to release an RPC session admission lease.",
                        connection.DisplayName);
                }
            }
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
