using Lakona.Game.Abstractions;
using System.Collections.Concurrent;

namespace Lakona.Game.Server.Sessions;

public sealed class InMemoryGameSessionRegistry : IGameSessionRegistry
{
    private const int MaxSessionItemKeyLength = 128;

    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<GameSessionKey, SessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, GameSessionKey> _connectionToSession = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GameSessionKey> _terminatedConnectionToSession = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Type, object>> _legacyCallbacksByConnection = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _ownerGenerations = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _resumeWindow;

    public InMemoryGameSessionRegistry()
        : this(new Lakona.Game.Server.Configuration.LakonaGameHostingOptions(), TimeProvider.System)
    {
    }

    public InMemoryGameSessionRegistry(
        Lakona.Game.Server.Configuration.LakonaGameHostingOptions hosting,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(hosting);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _resumeWindow = hosting.Sessions.ResumeWindow > TimeSpan.Zero
            ? hosting.Sessions.ResumeWindow
            : throw new ArgumentOutOfRangeException(nameof(hosting), "Session resume window must be positive.");
    }

    public ValueTask<GameSessionKey> StartNewSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        cancellationToken.ThrowIfCancellationRequested();

        var generation = _ownerGenerations.AddOrUpdate(ownerKey, 1, static (_, current) => checked(current + 1));
        var session = new GameSessionKey(ownerKey, Guid.NewGuid().ToString("N"), generation);
        if (!_sessions.TryAdd(session, new SessionState(session, ownerKey)))
        {
            throw new InvalidOperationException("Generated a duplicate game session id.");
        }

        return new ValueTask<GameSessionKey>(session);
    }

    public ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<SessionResumeDecision>(
                SessionResumeDecision.StateLost("Session was not found."));
        }

        lock (state.Gate)
        {
            if (state.Termination is not null)
            {
                return new ValueTask<SessionResumeDecision>(state.KeepTerminationForResume
                    ? SessionResumeDecision.Terminated(state.Session, state.Termination)
                    : SessionResumeDecision.StateLost("Session was terminated."));
            }

            if (state.ResumeDeadlineUtc is { } deadline && _timeProvider.GetUtcNow() >= deadline)
            {
                return new ValueTask<SessionResumeDecision>(
                    SessionResumeDecision.StateLost("Session resume window expired."));
            }

            return new ValueTask<SessionResumeDecision>(SessionResumeDecision.Resumed(state.Session));
        }
    }

    public ValueTask SetReliablePushPolicyAsync(
        GameSessionKey session,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(session, out var state))
        {
            throw new InvalidOperationException($"Game session '{session}' does not exist.");
        }

        lock (state.Gate)
        {
            if (state.ReliablePushPolicy is { } current && current != enabled)
            {
                throw new InvalidOperationException("Game session reliable-push policy does not match the endpoint.");
            }

            state.ReliablePushPolicy = enabled;
        }

        return default;
    }

    public ValueTask<bool> GetReliablePushPolicyAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<bool>(false);
        }

        lock (state.Gate)
        {
            return new ValueTask<bool>(state.ReliablePushPolicy == true);
        }
    }

    public ValueTask MarkReliableContinuityLostAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (_sessions.TryGetValue(session, out var state))
        {
            lock (state.Gate)
            {
                state.ReliableContinuityLost = true;
            }
        }

        return default;
    }

    public ValueTask<bool> IsReliableContinuityLostAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<bool>(false);
        }

        lock (state.Gate)
        {
            return new ValueTask<bool>(state.ReliableContinuityLost);
        }
    }

    public ValueTask<bool> IsReliableReplayPendingAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<bool>(false);
        }

        lock (state.Gate)
        {
            return new ValueTask<bool>(state.ReliableReplayPending);
        }
    }

    public ValueTask MarkReliableReplayReadyAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (_sessions.TryGetValue(session, out var state))
        {
            lock (state.Gate)
            {
                state.ReliableReplayPending = false;
            }
        }

        return default;
    }

    public ValueTask<GameSessionBindResult> BindSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new ValueTask<GameSessionBindResult>(BindSessionCore(session, connectionId));
        }
    }

    public ValueTask<GameSessionBindResult> BindSessionAsync<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateSession(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var result = BindSessionCore(session, connectionId);
            GetLegacyCallbacks(connectionId)[typeof(TCallback)] = callback;
            return new ValueTask<GameSessionBindResult>(result);
        }
    }

    public ValueTask<GameSessionBindResult> BindSessionCallbackAsync(
        GameSessionKey session,
        string connectionId,
        Type callbackContractType,
        object callback,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(callbackContractType);
        ArgumentNullException.ThrowIfNull(callback);
        if (!callbackContractType.IsInstanceOfType(callback))
            throw new ArgumentException("Callback does not implement the requested contract.", nameof(callback));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var result = BindSessionCore(session, connectionId);
            GetLegacyCallbacks(connectionId)[callbackContractType] = callback;
            return new ValueTask<GameSessionBindResult>(result);
        }
    }

    public ValueTask<GameSessionBindResult> BindCurrentSessionAsync<TCallback>(
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var session) ||
                !_sessions.ContainsKey(session))
            {
                throw new InvalidOperationException(
                    $"RPC connection '{connectionId}' does not have an active game session.");
            }

            var result = BindSessionCore(session, connectionId);
            GetLegacyCallbacks(connectionId)[typeof(TCallback)] = callback;
            return new ValueTask<GameSessionBindResult>(result);
        }
    }

    public ValueTask<GameSessionKey?> GetCurrentSessionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_connectionToSession.TryGetValue(connectionId, out var session) ||
            !_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<GameSessionKey?>((GameSessionKey?)null);
        }

        lock (state.Gate)
        {
            return new ValueTask<GameSessionKey?>(
                string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal) &&
                state.DisconnectedAt is null && state.Termination is null
                    ? session
                    : null);
        }
    }

    public ValueTask<string?> GetConnectionIdAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<string?>((string?)null);
        }

        lock (state.Gate)
        {
            return new ValueTask<string?>(state.DisconnectedAt is null ? state.ConnectionId : null);
        }
    }

    public ValueTask<IReadOnlyList<Type>> GetCallbackContractTypesAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<Type>>(Array.Empty<Type>());
    }

    public ValueTask SetSessionItemAsync(
        GameSessionKey session,
        string key,
        GameSessionItemValue value,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ValidateSessionItemKey(key);
        if (!value.IsDefined)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Session item value must be defined.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(session, out var state))
        {
            throw new InvalidOperationException($"Game session '{session}' does not exist.");
        }

        lock (state.Gate)
        {
            if (state.Termination is not null)
            {
                throw new InvalidOperationException($"Game session '{session}' is terminated.");
            }

            state.Items[key] = value;
            state.ItemsSnapshot = new GameSessionItems(state.Items);
        }

        return default;
    }

    public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ValidateSessionItemKey(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<GameSessionItemValue?>((GameSessionItemValue?)null);
        }

        var snapshot = Volatile.Read(ref state.ItemsSnapshot);
        return new ValueTask<GameSessionItemValue?>(snapshot.TryGetValue(key, out var value)
            ? value
            : (GameSessionItemValue?)null);
    }

    public ValueTask<GameSessionItems> GetSessionItemsAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<GameSessionItems>(GameSessionItems.Empty);
        }

        return new ValueTask<GameSessionItems>(Volatile.Read(ref state.ItemsSnapshot));
    }

    public ValueTask RemoveSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ValidateSessionItemKey(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(session, out var state))
        {
            throw new InvalidOperationException($"Game session '{session}' does not exist.");
        }

        lock (state.Gate)
        {
            if (state.Termination is not null)
            {
                throw new InvalidOperationException($"Game session '{session}' is terminated.");
            }

            if (state.Items.Remove(key))
            {
                state.ItemsSnapshot = state.Items.Count == 0
                    ? GameSessionItems.Empty
                    : new GameSessionItems(state.Items);
            }
        }

        return default;
    }

    public ValueTask<GameSessionSnapshot?> MarkConnectionDisconnectedAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var session) ||
                !_sessions.TryGetValue(session, out var state))
            {
                return new ValueTask<GameSessionSnapshot?>((GameSessionSnapshot?)null);
            }

            var snapshot = CreateSnapshot(state, connectionId);
            DisconnectState(state, connectionId, _timeProvider.GetUtcNow());
            return new ValueTask<GameSessionSnapshot?>(snapshot);
        }
    }

    public ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
            {
                return default;
            }

            if (connectionId is not null
                && !string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return default;
            }

            var activeConnectionId = state.ConnectionId;
            if (activeConnectionId is null)
            {
                return default;
            }

            DisconnectState(state, activeConnectionId, _timeProvider.GetUtcNow());
        }

        return default;
    }

    public ValueTask MarkSessionTerminatedAsync(
        GameSessionKey session,
        SessionTerminationNotice notice,
        bool keepForResume,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(notice);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
            {
                throw new InvalidOperationException($"Game session '{session}' does not exist.");
            }

            lock (state.Gate)
            {
                var activeConnectionId = state.ConnectionId;
                if (activeConnectionId is not null)
                {
                    _connectionToSession.TryRemove(activeConnectionId, out _);
                    _legacyCallbacksByConnection.TryRemove(activeConnectionId, out _);
                    _terminatedConnectionToSession[activeConnectionId] = session;
                    state.LastTerminatedConnectionId = activeConnectionId;
                }

                state.ConnectionId = null;
                state.Items.Clear();
                state.ItemsSnapshot = GameSessionItems.Empty;
                state.Termination = notice;
                state.KeepTerminationForResume = keepForResume;
            }
        }

        return default;
    }

    public ValueTask<GameSessionHeartbeatResult> RecordHeartbeatAsync(
        string connectionId,
        DateTimeOffset heartbeatAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_connectionToSession.TryGetValue(connectionId, out var session))
        {
            if (!_sessions.TryGetValue(session, out var activeState))
            {
                _connectionToSession.TryRemove(connectionId, out _);
                return new ValueTask<GameSessionHeartbeatResult>(GameSessionHeartbeatResult.StateLost());
            }

            lock (activeState.Gate)
            {
                if (activeState.Termination is null)
                {
                    activeState.LastHeartbeatAt = heartbeatAt;
                    return new ValueTask<GameSessionHeartbeatResult>(
                        GameSessionHeartbeatResult.ActiveSession(activeState.Session));
                }
            }
        }

        if (_terminatedConnectionToSession.TryGetValue(connectionId, out var terminatedSession) &&
            _sessions.TryGetValue(terminatedSession, out var terminatedState))
        {
            lock (terminatedState.Gate)
            {
                if (string.Equals(terminatedState.LastTerminatedConnectionId, connectionId, StringComparison.Ordinal) &&
                    terminatedState.Termination is { } termination)
                {
                    terminatedState.LastHeartbeatAt = heartbeatAt;
                    return new ValueTask<GameSessionHeartbeatResult>(
                        GameSessionHeartbeatResult.Terminated(terminatedState.Session, termination));
                }
            }
        }

        return new ValueTask<GameSessionHeartbeatResult>(GameSessionHeartbeatResult.ConnectionOnly());
    }

    public GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        var totalSessions = _sessions.Count;
        var activeSessions = 0;
        var activeConnections = 0;
        var disconnectedSessions = 0;
        var terminatedSessions = 0;
        var resumableSessions = 0;

        foreach (var state in _sessions.Values)
        {
            lock (state.Gate)
            {
                if (state.Termination is not null)
                {
                    terminatedSessions++;

                    if (state.KeepTerminationForResume)
                    {
                        resumableSessions++;
                    }

                    continue;
                }

                resumableSessions++;

                if (state.ConnectionId is not null && state.DisconnectedAt is null)
                {
                    activeSessions++;
                    activeConnections++;
                }
                else if (state.DisconnectedAt is not null)
                {
                    disconnectedSessions++;
                }
            }

        }

        return new GameSessionDiagnosticsSnapshot(
            totalSessions,
            activeSessions,
            activeConnections,
            disconnectedSessions,
            terminatedSessions,
            resumableSessions);
    }

    public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<TCallback?>((TCallback?)null);
        }

        string? connectionId;
        lock (state.Gate)
        {
            connectionId = state.DisconnectedAt is null ? state.ConnectionId : null;
        }

        if (connectionId is null ||
            !_legacyCallbacksByConnection.TryGetValue(connectionId, out var callbacks))
        {
            return new ValueTask<TCallback?>((TCallback?)null);
        }

        var callback = callbacks.TryGetValue(typeof(TCallback), out var exact)
            ? exact as TCallback
            : callbacks.Values.OfType<TCallback>().FirstOrDefault();
        return new ValueTask<TCallback?>(callback);
    }

    public ValueTask<GameSessionBinding<TCallback>?> GetSessionBindingAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_sessions.TryGetValue(session, out var state))
        {
            return new ValueTask<GameSessionBinding<TCallback>?>((GameSessionBinding<TCallback>?)null);
        }

        string? connectionId;
        lock (state.Gate)
        {
            connectionId = state.DisconnectedAt is null ? state.ConnectionId : null;
        }

        if (connectionId is null ||
            !_legacyCallbacksByConnection.TryGetValue(connectionId, out var callbacks))
        {
            return new ValueTask<GameSessionBinding<TCallback>?>((GameSessionBinding<TCallback>?)null);
        }

        var callback = callbacks.TryGetValue(typeof(TCallback), out var exact)
            ? exact as TCallback
            : callbacks.Values.OfType<TCallback>().FirstOrDefault();
        return callback is null
            ? new ValueTask<GameSessionBinding<TCallback>?>((GameSessionBinding<TCallback>?)null)
            : new ValueTask<GameSessionBinding<TCallback>?>(new GameSessionBinding<TCallback>(session, connectionId, callback));
    }

    public ValueTask<IReadOnlyList<GameSessionSnapshot>> ExpireDisconnectedSessionsAsync(
        DateTimeOffset disconnectedBefore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expired = new List<GameSessionSnapshot>();
        foreach (var item in _sessions)
        {
            var state = item.Value;
            string? connectionId;
            lock (state.Gate)
            {
                if (state.DisconnectedAt is null || state.DisconnectedAt >= disconnectedBefore)
                {
                    continue;
                }

                connectionId = state.LastDisconnectedConnectionId;
                if (connectionId is null)
                {
                    continue;
                }
            }

            lock (_gate)
            {
                lock (state.Gate)
                {
                    if (state.DisconnectedAt is null || state.DisconnectedAt >= disconnectedBefore ||
                        !_sessions.TryRemove(item.Key, out _))
                    {
                        continue;
                    }

                    connectionId = state.LastDisconnectedConnectionId;
                }

                if (connectionId is not null)
                {
                    expired.Add(CreateSnapshot(state, connectionId));
                }
            }
        }

        return new ValueTask<IReadOnlyList<GameSessionSnapshot>>(expired);
    }

    private void DisconnectState(SessionState state, string connectionId, DateTimeOffset disconnectedAt)
    {
        lock (state.Gate)
        {
            _connectionToSession.TryRemove(connectionId, out _);
            _legacyCallbacksByConnection.TryRemove(connectionId, out _);
            state.ConnectionId = null;
            state.LastDisconnectedConnectionId = connectionId;
            state.DisconnectedAt = disconnectedAt;
            state.ResumeDeadlineUtc = disconnectedAt.Add(_resumeWindow);
        }
    }

    private static GameSessionSnapshot CreateSnapshot(SessionState state, string connectionId)
    {
        return new GameSessionSnapshot(state.Session, connectionId);
    }

    private GameSessionBindResult BindSessionCore(
        GameSessionKey session,
        string connectionId)
    {
        if (!_sessions.TryGetValue(session, out var state))
        {
            throw new InvalidOperationException($"Game session '{session}' does not exist.");
        }

        lock (state.Gate)
        {
            if (state.Termination is not null)
            {
                throw new InvalidOperationException($"Game session '{session}' is terminated.");
            }

            if (_connectionToSession.TryGetValue(connectionId, out var boundSession)
                && boundSession != session)
            {
                throw new InvalidOperationException($"RPC connection '{connectionId}' is already bound to game session '{boundSession}'.");
            }

            var previousConnectionId = state.ConnectionId;
            var sessionBecameActive = previousConnectionId is null;
            if (!string.Equals(previousConnectionId, connectionId, StringComparison.Ordinal))
            {
                if (previousConnectionId is not null)
                {
                    _connectionToSession.TryRemove(previousConnectionId, out _);
                    _legacyCallbacksByConnection.TryRemove(previousConnectionId, out _);
                }

                state.ConnectionId = connectionId;
                _connectionToSession[connectionId] = session;
            }

            if (sessionBecameActive && state.ResumeDeadlineUtc.HasValue && state.ReliablePushPolicy == true)
            {
                state.ReliableReplayPending = true;
            }
            state.LastDisconnectedConnectionId = null;
            if (state.LastTerminatedConnectionId is { } terminatedConnectionId)
            {
                _terminatedConnectionToSession.TryRemove(terminatedConnectionId, out _);
            }
            state.LastTerminatedConnectionId = null;
            state.DisconnectedAt = null;
            state.ResumeDeadlineUtc = null;
            state.LastHeartbeatAt = _timeProvider.GetUtcNow();

            return new GameSessionBindResult(sessionBecameActive
                ? CreateSnapshot(state, connectionId)
                : null);
        }
    }

    private ConcurrentDictionary<Type, object> GetLegacyCallbacks(string connectionId)
    {
        return _legacyCallbacksByConnection.GetOrAdd(
            connectionId,
            static _ => new ConcurrentDictionary<Type, object>());
    }

    private static void ValidateSession(GameSessionKey session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.OwnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.Generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(session), "Session generation must be positive.");
        }
    }

    private static void ValidateSessionItemKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > MaxSessionItemKeyLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                $"Session item key length must be less than or equal to {MaxSessionItemKeyLength}.");
        }
    }

    private sealed class SessionState
    {
        public SessionState(GameSessionKey session, string ownerKey)
        {
            Session = session;
            OwnerKey = ownerKey;
        }

        public GameSessionKey Session { get; }

        public Lock Gate { get; } = new();

        public string OwnerKey { get; }

        public string? ConnectionId { get; set; }

        public string? LastDisconnectedConnectionId { get; set; }

        public string? LastTerminatedConnectionId { get; set; }

        public DateTimeOffset? DisconnectedAt { get; set; }

        public DateTimeOffset? ResumeDeadlineUtc { get; set; }

        public bool? ReliablePushPolicy { get; set; }

        public bool ReliableContinuityLost { get; set; }

        public bool ReliableReplayPending { get; set; }

        public DateTimeOffset? LastHeartbeatAt { get; set; }

        public SessionTerminationNotice? Termination { get; set; }

        public bool KeepTerminationForResume { get; set; }

        public Dictionary<string, GameSessionItemValue> Items { get; } = new(StringComparer.Ordinal);

        public GameSessionItems ItemsSnapshot = GameSessionItems.Empty;
    }
}
