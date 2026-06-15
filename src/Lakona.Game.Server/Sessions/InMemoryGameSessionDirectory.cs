using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class InMemoryGameSessionDirectory : IGameSessionDirectory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<GameSessionKey, SessionState> _sessions = new();
    private readonly Dictionary<string, GameSessionKey> _connectionToSession = new(StringComparer.Ordinal);

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
                    ? SessionResumeDecision.Terminated(state.Termination)
                    : SessionResumeDecision.StateLost("Session was terminated."));
            }

            return new ValueTask<SessionResumeDecision>(SessionResumeDecision.Resumed(state.Session));
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

            state.LastDisconnectedConnectionId = null;
            state.DisconnectedAt = null;
            state.Callbacks[typeof(TCallback)] = callback;

            return new ValueTask<GameSessionBindResult>(new GameSessionBindResult(sessionBecameActive
                ? CreateSnapshot(state, connectionId)
                : null));
        }
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
            DisconnectState(state, connectionId, DateTimeOffset.UtcNow);
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

            DisconnectState(state, activeConnectionId, DateTimeOffset.UtcNow);
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

            if (state.ConnectionId is not null)
            {
                _connectionToSession.Remove(state.ConnectionId);
            }

            state.ConnectionId = null;
            state.Callbacks.Clear();
            state.Termination = notice;
            state.KeepTerminationForResume = keepForResume;
        }

        return default;
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

    private static void ValidateSession(GameSessionKey session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.OwnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.Generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(session), "Session generation must be positive.");
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

        public DateTimeOffset? DisconnectedAt { get; set; }

        public IReadOnlyList<Type> DisconnectedCallbackContractTypes { get; set; } = Array.Empty<Type>();

        public SessionTerminationNotice? Termination { get; set; }

        public bool KeepTerminationForResume { get; set; }

        public Dictionary<Type, object> Callbacks { get; } = new();
    }
}
