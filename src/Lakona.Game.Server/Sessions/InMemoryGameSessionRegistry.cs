using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class InMemoryGameSessionRegistry : IGameSessionRegistry
{
    private const int MaxSessionItemKeyLength = 128;

    private readonly Lock _gate = new();
    private readonly Dictionary<GameSessionKey, SessionState> _sessions = new();
    private readonly Dictionary<string, GameSessionKey> _connectionToSession = new(StringComparer.Ordinal);
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

        lock (_gate)
        {
            var generation = _sessions.Keys
                .Where(session => string.Equals(session.OwnerKey, ownerKey, StringComparison.Ordinal))
                .Select(session => session.Generation)
                .DefaultIfEmpty(0)
                .Max() + 1;
            var session = new GameSessionKey(ownerKey, Guid.NewGuid().ToString("N"), generation);
            _sessions.Add(session, new SessionState(session, ownerKey));
            return new ValueTask<GameSessionKey>(session);
        }
    }

    public ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
            {
                return new ValueTask<SessionResumeDecision>(
                    SessionResumeDecision.StateLost("Session was not found."));
            }

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
        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
            {
                throw new InvalidOperationException($"Game session '{session}' does not exist.");
            }

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
        lock (_gate)
        {
            return new ValueTask<bool>(
                _sessions.TryGetValue(session, out var state) && state.ReliablePushPolicy == true);
        }
    }

    public ValueTask MarkReliableContinuityLostAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_sessions.TryGetValue(session, out var state))
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
        lock (_gate)
        {
            return new ValueTask<bool>(
                _sessions.TryGetValue(session, out var state) && state.ReliableContinuityLost);
        }
    }

    public ValueTask<bool> IsReliableReplayPendingAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new ValueTask<bool>(
                _sessions.TryGetValue(session, out var state) && state.ReliableReplayPending);
        }
    }

    public ValueTask MarkReliableReplayReadyAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_sessions.TryGetValue(session, out var state))
            {
                state.ReliableReplayPending = false;
            }
        }

        return default;
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
            return new ValueTask<GameSessionBindResult>(BindSessionCore(session, connectionId, callback));
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
            return new ValueTask<GameSessionBindResult>(
                BindSessionCore(session, connectionId, callbackContractType, callback));
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

            return new ValueTask<GameSessionBindResult>(BindSessionCore(session, connectionId, callback));
        }
    }

    public ValueTask<GameSessionKey?> GetCurrentSessionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return new ValueTask<GameSessionKey?>(
                _connectionToSession.TryGetValue(connectionId, out var session) &&
                _sessions.ContainsKey(session)
                    ? session
                    : null);
        }
    }

    public ValueTask<IReadOnlyList<Type>> GetCallbackContractTypesAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
                return new ValueTask<IReadOnlyList<Type>>(Array.Empty<Type>());

            IReadOnlyList<Type> result = state.Callbacks.Count == 0
                ? state.DisconnectedCallbackContractTypes.ToArray()
                : state.Callbacks.Keys.ToArray();
            return new ValueTask<IReadOnlyList<Type>>(result);
        }
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

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
            {
                throw new InvalidOperationException($"Game session '{session}' does not exist.");
            }

            if (state.Termination is not null)
            {
                throw new InvalidOperationException($"Game session '{session}' is terminated.");
            }

            state.Items[key] = value;
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

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state) ||
                state.Termination is not null ||
                !state.Items.TryGetValue(key, out var value))
            {
                return new ValueTask<GameSessionItemValue?>((GameSessionItemValue?)null);
            }

            return new ValueTask<GameSessionItemValue?>(value);
        }
    }

    public ValueTask<GameSessionItems> GetSessionItemsAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state) ||
                state.Termination is not null ||
                state.Items.Count == 0)
            {
                return new ValueTask<GameSessionItems>(GameSessionItems.Empty);
            }

            return new ValueTask<GameSessionItems>(new GameSessionItems(state.Items));
        }
    }

    public ValueTask RemoveSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ValidateSessionItemKey(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
            {
                throw new InvalidOperationException($"Game session '{session}' does not exist.");
            }

            if (state.Termination is not null)
            {
                throw new InvalidOperationException($"Game session '{session}' is terminated.");
            }

            state.Items.Remove(key);
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

            var activeConnectionId = state.ConnectionId;
            if (activeConnectionId is not null)
            {
                _connectionToSession.Remove(activeConnectionId);
                state.LastTerminatedConnectionId = activeConnectionId;
            }

            state.ConnectionId = null;
            state.Callbacks.Clear();
            state.Items.Clear();
            state.Termination = notice;
            state.KeepTerminationForResume = keepForResume;
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

        lock (_gate)
        {
            if (_connectionToSession.TryGetValue(connectionId, out var session))
            {
                if (!_sessions.TryGetValue(session, out var activeState))
                {
                    _connectionToSession.Remove(connectionId);
                    return new ValueTask<GameSessionHeartbeatResult>(GameSessionHeartbeatResult.StateLost());
                }

                if (activeState.Termination is null)
                {
                    activeState.LastHeartbeatAt = heartbeatAt;
                    return new ValueTask<GameSessionHeartbeatResult>(
                        GameSessionHeartbeatResult.ActiveSession(activeState.Session));
                }
            }

            foreach (var state in _sessions.Values)
            {
                if (string.Equals(state.LastTerminatedConnectionId, connectionId, StringComparison.Ordinal) &&
                    state.Termination is not null)
                {
                    state.LastHeartbeatAt = heartbeatAt;
                    return new ValueTask<GameSessionHeartbeatResult>(
                        GameSessionHeartbeatResult.Terminated(state.Session, state.Termination));
                }
            }

            return new ValueTask<GameSessionHeartbeatResult>(GameSessionHeartbeatResult.ConnectionOnly());
        }
    }

    public GameSessionDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        lock (_gate)
        {
            var totalSessions = _sessions.Count;
            var activeSessions = 0;
            var activeConnections = 0;
            var disconnectedSessions = 0;
            var terminatedSessions = 0;
            var resumableSessions = 0;

            foreach (var state in _sessions.Values)
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

            return new GameSessionDiagnosticsSnapshot(
                totalSessions,
                activeSessions,
                activeConnections,
                disconnectedSessions,
                terminatedSessions,
                resumableSessions);
        }
    }

    public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state) ||
                state.DisconnectedAt is not null ||
                !TryGetCallback(state, out TCallback? callback))
            {
                return new ValueTask<TCallback?>((TCallback?)null);
            }

            return new ValueTask<TCallback?>(callback);
        }
    }

    public ValueTask<GameSessionBinding<TCallback>?> GetSessionBindingAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state) ||
                state.ConnectionId is null ||
                state.DisconnectedAt is not null ||
                !TryGetCallback(state, out TCallback? typedCallback))
            {
                return new ValueTask<GameSessionBinding<TCallback>?>((GameSessionBinding<TCallback>?)null);
            }

            return new ValueTask<GameSessionBinding<TCallback>?>(
                new GameSessionBinding<TCallback>(session, state.ConnectionId, typedCallback!));
        }
    }

    public ValueTask<IReadOnlyList<GameSessionSnapshot>> ExpireDisconnectedSessionsAsync(
        DateTimeOffset disconnectedBefore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var expired = new List<GameSessionSnapshot>();
            foreach (var item in _sessions.ToArray())
            {
                var state = item.Value;
                if (state.DisconnectedAt is null || state.DisconnectedAt >= disconnectedBefore)
                {
                    continue;
                }

                var connectionId = state.LastDisconnectedConnectionId;
                if (connectionId is null)
                {
                    continue;
                }

                expired.Add(CreateSnapshot(state, connectionId));
                if (state.ConnectionId is not null)
                {
                    _connectionToSession.Remove(state.ConnectionId);
                }

                _sessions.Remove(item.Key);
            }

            return new ValueTask<IReadOnlyList<GameSessionSnapshot>>(expired);
        }
    }

    private void DisconnectState(SessionState state, string connectionId, DateTimeOffset disconnectedAt)
    {
        _connectionToSession.Remove(connectionId);
        state.ConnectionId = null;
        state.LastDisconnectedConnectionId = connectionId;
        state.DisconnectedAt = disconnectedAt;
        state.ResumeDeadlineUtc = disconnectedAt.Add(_resumeWindow);
        state.DisconnectedCallbackContractTypes = state.Callbacks.Keys.ToArray();
        state.Callbacks.Clear();
    }

    private static GameSessionSnapshot CreateSnapshot(SessionState state, string connectionId)
    {
        return new GameSessionSnapshot(
            state.Session,
            connectionId,
            state.Callbacks.Count == 0
                ? state.DisconnectedCallbackContractTypes
                : state.Callbacks.Keys.ToArray());
    }

    private static bool TryGetCallback<TCallback>(SessionState state, out TCallback? callback)
        where TCallback : class
    {
        if (state.Callbacks.TryGetValue(typeof(TCallback), out var exact) &&
            exact is TCallback exactCallback)
        {
            callback = exactCallback;
            return true;
        }

        foreach (var item in state.Callbacks.Values)
        {
            if (item is TCallback assignableCallback)
            {
                callback = assignableCallback;
                return true;
            }
        }

        callback = null;
        return false;
    }

    private GameSessionBindResult BindSessionCore<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback)
        where TCallback : class
        => BindSessionCore(session, connectionId, typeof(TCallback), callback);

    private GameSessionBindResult BindSessionCore(
        GameSessionKey session,
        string connectionId,
        Type callbackContractType,
        object callback)
    {
        if (!_sessions.TryGetValue(session, out var state))
        {
            throw new InvalidOperationException($"Game session '{session}' does not exist.");
        }

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
                _connectionToSession.Remove(previousConnectionId);
                state.Callbacks.Clear();
            }

            state.ConnectionId = connectionId;
            _connectionToSession[connectionId] = session;
        }

        if (sessionBecameActive && state.ResumeDeadlineUtc.HasValue && state.ReliablePushPolicy == true)
        {
            state.ReliableReplayPending = true;
        }
        state.LastDisconnectedConnectionId = null;
        state.LastTerminatedConnectionId = null;
        state.DisconnectedAt = null;
        state.ResumeDeadlineUtc = null;
        state.LastHeartbeatAt = _timeProvider.GetUtcNow();
        state.Callbacks[callbackContractType] = callback;

        return new GameSessionBindResult(sessionBecameActive
            ? CreateSnapshot(state, connectionId)
            : null);
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

        public IReadOnlyList<Type> DisconnectedCallbackContractTypes { get; set; } = Array.Empty<Type>();

        public SessionTerminationNotice? Termination { get; set; }

        public bool KeepTerminationForResume { get; set; }

        public Dictionary<Type, object> Callbacks { get; } = new();

        public Dictionary<string, GameSessionItemValue> Items { get; } = new(StringComparer.Ordinal);
    }
}
