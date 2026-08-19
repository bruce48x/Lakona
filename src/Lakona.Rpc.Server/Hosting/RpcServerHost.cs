using System.Collections.Concurrent;
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
    private readonly TimeSpan _shutdownTimeout;
    private readonly RpcServiceRegistry _registry;
    private readonly IReadOnlyList<IRpcSessionAdmissionGate> _sessionAdmissionGates;
    private readonly IReadOnlyList<IRpcSessionRequestGate> _requestGates;
    private readonly IReadOnlyList<IRpcSessionLifecycleObserver> _sessionLifecycleObservers;
    private readonly IReadOnlyList<IRpcServerLifecycleObserver> _serverLifecycleObservers;
    private readonly TransportSecurityConfig _security;
    private readonly IRpcSerializer _serializer;
    private readonly ConcurrentDictionary<string, RpcSession> _activeSessions = new();
    private int _activeConnections;
    internal RpcServerHost(
        IRpcSerializer serializer,
        RpcServiceRegistry registry,
        TransportSecurityConfig security,
        RpcKeepAliveOptions keepAlive,
        Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> acceptorFactory,
        ILogger logger,
        RpcServerLimits limits,
        TimeSpan shutdownTimeout,
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
        _shutdownTimeout = shutdownTimeout;
        _sessionAdmissionGates = sessionAdmissionGates ?? Array.Empty<IRpcSessionAdmissionGate>();
        _requestGates = requestGates ?? Array.Empty<IRpcSessionRequestGate>();
        _sessionLifecycleObservers = sessionLifecycleObservers ?? Array.Empty<IRpcSessionLifecycleObserver>();
        _serverLifecycleObservers = serverLifecycleObservers ?? Array.Empty<IRpcServerLifecycleObserver>();
    }

    /// <summary>Runs the host until cancellation and then drains active Sessions.</summary>
    /// <exception cref="RpcServerShutdownTimeoutException">
    ///     Thrown when cooperative Session cleanup exceeds the configured shutdown timeout.
    /// </exception>
    public async ValueTask RunAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var connectionTasks = new TrackedTaskCollection();
        var shutdownTimedOut = false;

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
            using var shutdownDeadline = new CancellationTokenSource(_shutdownTimeout);
            try
            {
                await connectionTasks.WaitAsync(shutdownDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdownDeadline.IsCancellationRequested)
            {
                shutdownTimedOut = true;
                var activeSessions = _activeSessions.Values.ToArray();
                AbortSessions(activeSessions);
                _logger.LogError(
                    "RPC server shutdown exceeded {ShutdownTimeout}; aborting {ActiveSessionCount} active Session transport(s).",
                    _shutdownTimeout,
                    activeSessions.Length);
                throw new RpcServerShutdownTimeoutException(_shutdownTimeout, activeSessions.Length);
            }
        }
        finally
        {
            if (shutdownTimedOut)
                _logger.LogWarning("Server run ended with incomplete Session cleanup after shutdown timeout.");
            else
                _logger.LogInformation("Server stopped.");
        }
    }

    private void AbortSessions(IReadOnlyList<RpcSession> sessions)
    {
        foreach (var session in sessions)
        {
            try
            {
                var abort = session.AbortTransportAsync();
                if (!abort.IsCompletedSuccessfully)
                    _ = ObserveAbortAsync(session.ConnectionId, abort);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to abort RPC Session transport for connection {ConnectionId} after shutdown timeout.",
                    session.ConnectionId);
            }
        }
    }

    private async Task ObserveAbortAsync(string connectionId, ValueTask abort)
    {
        try
        {
            await abort.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to abort RPC Session transport for connection {ConnectionId} after shutdown timeout.",
                connectionId);
        }
    }

    private async Task RunAdmittedConnectionAsync(
        RpcAcceptedConnection connection,
        CancellationToken hostCt)
    {
        (RpcSessionLifecycleContext Context, Exception? Error)? completion;
        try
        {
            completion = await RunConnectionAsync(connection, hostCt).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }

        if (completion is not { } completed)
            return;

        await NotifySessionDisconnectedAsync(completed.Context, completed.Error, CancellationToken.None)
            .ConfigureAwait(false);
        _logger.LogInformation("[{DisplayName}] disconnected.", connection.DisplayName);
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

    private async Task<(RpcSessionLifecycleContext Context, Exception? Error)?> RunConnectionAsync(
        RpcAcceptedConnection connection,
        CancellationToken hostCt)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var admissionLeases = new List<IAsyncDisposable>(_sessionAdmissionGates.Count);
        var lifetimeTokens = new List<CancellationToken>(_sessionAdmissionGates.Count + 1) { hostCt };
        RpcSessionLifecycleContext? lifecycleContext = null;
        Exception? disconnectError = null;

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
                    return null;
                }

                if (!result.IsAllowed)
                {
                    _logger.LogWarning(
                        "[{DisplayName}] RPC session admission rejected: {Reason}.",
                        connection.DisplayName,
                        result.RejectionReason);
                    await DisposeRejectedConnectionAsync(connection).ConfigureAwait(false);
                    return null;
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
            var session = new RpcSession(
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
            var sessionRegistered = false;
            try
            {
                if (!_activeSessions.TryAdd(connectionId, session))
                    throw new InvalidOperationException($"RPC Session '{connectionId}' is already active.");
                sessionRegistered = true;
                lifecycleContext = new RpcSessionLifecycleContext(connectionId, connection.DisplayName);
                session.Disconnected += ex => disconnectError = ex;

                await NotifySessionStartedAsync(lifecycleContext, sessionCt).ConfigureAwait(false);
                await session.RunAsync(sessionCt).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (sessionRegistered)
                        _activeSessions.TryRemove(connectionId, out _);
                }
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

        return lifecycleContext is null
            ? null
            : (lifecycleContext, disconnectError);
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
