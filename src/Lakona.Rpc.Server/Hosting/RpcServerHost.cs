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
    private readonly RpcServiceExceptionHandler? _serviceExceptionHandler;
    private readonly Func<IRpcServiceInvocationScope>? _openServiceScope;
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
        ILoggerFactory? loggerFactory = null,
        RpcServiceExceptionHandler? serviceExceptionHandler = null,
        Func<IRpcServiceInvocationScope>? openServiceScope = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        _acceptorFactory = acceptorFactory ?? throw new ArgumentNullException(nameof(acceptorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory;
        _serviceExceptionHandler = serviceExceptionHandler;
        _openServiceScope = openServiceScope;
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _shutdownTimeout = shutdownTimeout;
        _sessionAdmissionGates = sessionAdmissionGates ?? Array.Empty<IRpcSessionAdmissionGate>();
        _requestGates = requestGates ?? Array.Empty<IRpcSessionRequestGate>();
        _sessionLifecycleObservers = sessionLifecycleObservers ?? Array.Empty<IRpcSessionLifecycleObserver>();
        _serverLifecycleObservers = serverLifecycleObservers ?? Array.Empty<IRpcServerLifecycleObserver>();
    }

    /// <summary>Runs the host and drains active Sessions on cancellation or failure.</summary>
    /// <exception cref="RpcServerShutdownTimeoutException">
    ///     Thrown when cooperative Session cleanup exceeds the configured shutdown timeout.
    /// </exception>
    public async ValueTask RunAsync(CancellationToken ct = default)
    {
        using var listenerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // External cancellation stops admission only. Session cancellation callbacks
        // run under StopSessionsAsync's deadline, never on the caller's Cancel stack.
        // Session cleanup owns this source, including completion after a timeout.
        var sessionCts = new CancellationTokenSource();
        var connectionTasks = new TrackedTaskCollection();
        IRpcConnectionAcceptor? acceptor = null;
        Exception? runError = null;

        try
        {
            acceptor = await _acceptorFactory(listenerCts.Token).ConfigureAwait(false);
            var listener = new RpcServerListeningContext(acceptor.ListenAddress);
            // The wrapper takes ownership only after construction succeeds.
            acceptor = new BoundedConnectionAcceptor(
                acceptor,
                _limits.MaxPendingAcceptedConnections,
                _logger,
                listenerCts.Token,
                exception => NotifyListenerFailed(listener, exception));
            _logger.LogInformation(
                "RPC server listening on {ListenAddress}.",
                listener.ListenAddress);
            await NotifyListeningAsync(listener, listenerCts.Token).ConfigureAwait(false);

            while (!listenerCts.IsCancellationRequested)
            {
                RpcAcceptedConnection connection;
                try
                {
                    connection = await acceptor.AcceptAsync(listenerCts.Token).ConfigureAwait(false);
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
                    await DisposeRejectedTransportAsync(connection.Transport, connection.DisplayName).ConfigureAwait(false);
                    continue;
                }

                var connectionTask = RunAdmittedConnectionAsync(connection, sessionCts.Token);
                connectionTasks.Track(connectionTask);
            }
        }
        catch (Exception exception)
        {
            runError = exception;
            throw;
        }
        finally
        {
            try
            {
                if (acceptor is not null)
                    await acceptor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposalError)
            {
                if (runError is null)
                {
                    runError = disposalError;
                    throw;
                }

                _logger.LogError(disposalError, "RPC listener disposal failed after the host run failed.");
            }
            finally
            {
                await StopSessionsAsync(sessionCts, connectionTasks, runError).ConfigureAwait(false);
            }
        }
    }

    private async Task StopSessionsAsync(CancellationTokenSource cts, TrackedTaskCollection connectionTasks, Exception? runError)
    {
        var shutdownTimedOut = false;
        var cleanupCompleted = false;
        try
        {
            using var shutdownDeadline = new CancellationTokenSource(_shutdownTimeout);
            var cleanup = CompleteSessionCleanupAsync(cts, connectionTasks);
            try
            {
                await cleanup.WaitAsync(shutdownDeadline.Token).ConfigureAwait(false);
                cleanupCompleted = true;
            }
            catch (OperationCanceledException) when (shutdownDeadline.IsCancellationRequested)
            {
                shutdownTimedOut = true;
                _ = ObserveLateSessionCleanupAsync(cleanup);
                var activeSessions = _activeSessions.Values.ToArray();
                _logger.LogError(
                    "RPC server shutdown exceeded {ShutdownTimeout}; aborting {ActiveSessionCount} active Session transport(s).",
                    _shutdownTimeout,
                    activeSessions.Length);
                await AbortSessionsAsync(activeSessions, _shutdownTimeout).ConfigureAwait(false);
                throw new RpcServerShutdownTimeoutException(_shutdownTimeout, activeSessions.Length);
            }
        }
        catch (Exception cleanupError) when (runError is not null)
        {
            _logger.LogError(cleanupError, "RPC server cleanup failed after the host run failed.");
        }
        finally
        {
            if (shutdownTimedOut)
                _logger.LogWarning("Server run ended with incomplete Session cleanup after shutdown timeout.");
            else if (cleanupCompleted)
                _logger.LogInformation("Server stopped.");
            else
                _logger.LogWarning("Server run ended with failed Session cleanup.");
        }
    }

    private static async Task CompleteSessionCleanupAsync(CancellationTokenSource cts, TrackedTaskCollection connectionTasks)
    {
        try
        {
            // Join both even if a cancellation callback throws. CancelAsync marks the
            // token immediately, but callback completion is separate from Session drain.
            await Task.WhenAll(cts.CancelAsync(), connectionTasks.WaitAsync().AsTask()).ConfigureAwait(false);
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task ObserveLateSessionCleanupAsync(Task cleanup)
    {
        try
        {
            await cleanup.ConfigureAwait(false);
            _logger.LogWarning("RPC Session cleanup completed after its shutdown deadline.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "RPC Session cleanup completed after its shutdown deadline with an error.");
        }
    }

    private async Task AbortSessionsAsync(
        IReadOnlyList<RpcSession> sessions,
        TimeSpan abortJoinTimeout)
    {
        var aborts = new Task[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
            aborts[i] = AbortSessionAsync(sessions[i]);

        var allAborts = Task.WhenAll(aborts);
        try
        {
            await allAborts.WaitAsync(abortJoinTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogError(
                "RPC server forced transport abort did not complete within {AbortJoinTimeout}; " +
                "shutdown will report incomplete cleanup.",
                abortJoinTimeout);
            _ = ObserveLateAbortCompletionAsync(allAborts);
        }
    }

    private async Task ObserveLateAbortCompletionAsync(Task allAborts)
    {
        await allAborts.ConfigureAwait(false);
        _logger.LogWarning("RPC server forced transport abort completed after shutdown returned.");
    }

    private async Task AbortSessionAsync(RpcSession session)
    {
        try
        {
            await session.AbortTransportAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to abort RPC Session transport for connection {ConnectionId} after shutdown timeout.",
                session.ConnectionId);
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

    private async ValueTask DisposeRejectedTransportAsync(ITransport transport, string displayName)
    {
        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "[{DisplayName}] Failed to dispose a rejected RPC connection.",
                displayName);
        }
    }

    private async ValueTask NotifyListeningAsync(
        RpcServerListeningContext context,
        CancellationToken cancellationToken)
    {
        foreach (var observer in _serverLifecycleObservers)
        {
            await observer.OnListeningAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private void NotifyListenerFailed(RpcServerListeningContext context, Exception exception)
    {
        foreach (var observer in _serverLifecycleObservers)
        {
            try
            {
                observer.OnListenerFailed(context, exception);
            }
            catch (Exception observerError)
            {
                _logger.LogError(observerError, "Failed to notify an observer of the listener failure on {ListenAddress}.", context.ListenAddress);
            }
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
        ITransport? hostOwnedTransport = connection.Transport;

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
                    return null;
                }

                if (!result.IsAllowed)
                {
                    _logger.LogWarning(
                        "[{DisplayName}] RPC session admission rejected: {Reason}.",
                        connection.DisplayName,
                        result.RejectionReason);
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
            hostOwnedTransport = transport;
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
                remoteEndPoint: connection.RemoteEndPoint,
                serviceExceptionHandler: _serviceExceptionHandler,
                openServiceScope: _openServiceScope);
            hostOwnedTransport = null; // The Session now owns transport cleanup.
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
            if (hostOwnedTransport is not null)
                await DisposeRejectedTransportAsync(hostOwnedTransport, connection.DisplayName).ConfigureAwait(false);

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
