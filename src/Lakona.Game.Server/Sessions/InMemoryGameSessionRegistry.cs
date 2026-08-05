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
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _resumeWindow;
    private readonly IGameSessionIdFactory _sessionIds;

    public InMemoryGameSessionRegistry()
        : this(
            new Lakona.Game.Server.Configuration.LakonaGameHostingOptions(),
            TimeProvider.System,
            new RandomGameSessionIdFactory())
    {
    }

    public InMemoryGameSessionRegistry(
        Lakona.Game.Server.Configuration.LakonaGameHostingOptions hosting,
        TimeProvider timeProvider)
        : this(hosting, timeProvider, new RandomGameSessionIdFactory())
    {
    }

    public InMemoryGameSessionRegistry(
        Lakona.Game.Server.Configuration.LakonaGameHostingOptions hosting,
        TimeProvider timeProvider,
        IGameSessionIdFactory sessionIds)
    {
        ArgumentNullException.ThrowIfNull(hosting);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _sessionIds = sessionIds ?? throw new ArgumentNullException(nameof(sessionIds));
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

        var session = new GameSessionKey(ownerKey, _sessionIds.Create());
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
                if (state.TerminalRetentionDeadlineUtc is { } terminalDeadline &&
                    _timeProvider.GetUtcNow() >= terminalDeadline)
                {
                    return new ValueTask<SessionResumeDecision>(
                        SessionResumeDecision.StateLost("Session terminal outcome expired."));
                }

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
            var result = PrepareSessionBindingCore(session, connectionId);
            CommitSessionBindingCore(session, connectionId);
            return new ValueTask<GameSessionBindResult>(result);
        }
    }

    public ValueTask<GameSessionBindResult> PrepareSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return new ValueTask<GameSessionBindResult>(
                PrepareSessionBindingCore(session, connectionId));
        }
    }

    public ValueTask CommitSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            CommitSessionBindingCore(session, connectionId);
        }

        return default;
    }

    public ValueTask RollbackSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var state))
            {
                return default;
            }

            lock (state.Gate)
            {
                if (state.PendingBinding is not { } pending ||
                    !string.Equals(pending.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    return default;
                }

                RemoveConnectionMapping(_connectionToSession, connectionId, session);
                state.ConnectionId = pending.PreviousConnectionId;
                if (pending.PreviousConnectionId is not null)
                {
                    _connectionToSession[pending.PreviousConnectionId] = session;
                }

                state.LastDisconnectedConnectionId = pending.LastDisconnectedConnectionId;
                state.LastTerminatedConnectionId = pending.LastTerminatedConnectionId;
                if (pending.LastTerminatedConnectionId is not null)
                {
                    _terminatedConnectionToSession[pending.LastTerminatedConnectionId] = session;
                }

                state.DisconnectedAt = pending.DisconnectedAt;
                state.ResumeDeadlineUtc = pending.ResumeDeadlineUtc;
                state.ReliableReplayPending = pending.ReliableReplayPending;
                state.LastHeartbeatAt = pending.LastHeartbeatAt;
                state.PendingBinding = null;
            }
        }

        return default;
    }

    public ValueTask RemoveSessionAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sessions.TryRemove(session, out var state))
            {
                return default;
            }

            lock (state.Gate)
            {
                RemoveConnectionMapping(_connectionToSession, state.ConnectionId, session);
                RemoveConnectionMapping(_connectionToSession, state.LastDisconnectedConnectionId, session);
                RemoveConnectionMapping(_terminatedConnectionToSession, state.LastTerminatedConnectionId, session);
                state.ConnectionId = null;
                state.PendingBinding = null;
                state.Items.Clear();
                state.ItemsSnapshot = GameSessionItems.Empty;
            }
        }

        return default;
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
                state.DisconnectedAt is null && state.Termination is null &&
                state.PendingBinding is null
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
            if (state.PendingBinding is { } pending)
            {
                return new ValueTask<string?>(
                    pending.DisconnectedAt is null
                        ? pending.PreviousConnectionId
                        : null);
            }

            return new ValueTask<string?>(state.DisconnectedAt is null ? state.ConnectionId : null);
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
                if (!keepForResume)
                {
                    _sessions.TryRemove(session, out _);
                    RemoveConnectionMapping(_connectionToSession, activeConnectionId, session);
                    RemoveConnectionMapping(
                        _connectionToSession,
                        state.LastDisconnectedConnectionId,
                        session);
                    RemoveConnectionMapping(
                        _terminatedConnectionToSession,
                        state.LastTerminatedConnectionId,
                        session);
                    state.ConnectionId = null;
                    state.PendingBinding = null;
                    state.Items.Clear();
                    state.ItemsSnapshot = GameSessionItems.Empty;
                    return default;
                }

                if (activeConnectionId is not null)
                {
                    _connectionToSession.TryRemove(activeConnectionId, out _);
                    _terminatedConnectionToSession[activeConnectionId] = session;
                    state.LastTerminatedConnectionId = activeConnectionId;
                }

                state.ConnectionId = null;
                state.Items.Clear();
                state.ItemsSnapshot = GameSessionItems.Empty;
                state.Termination = notice;
                state.KeepTerminationForResume = keepForResume;
                var terminalDeadline = _timeProvider.GetUtcNow().Add(_resumeWindow);
                state.TerminalRetentionDeadlineUtc = state.ResumeDeadlineUtc is { } resumeDeadline &&
                    resumeDeadline < terminalDeadline
                        ? resumeDeadline
                        : terminalDeadline;
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
                if (activeState.Termination is null && activeState.PendingBinding is null)
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
                    if (terminatedState.TerminalRetentionDeadlineUtc is { } terminalDeadline &&
                        _timeProvider.GetUtcNow() >= terminalDeadline)
                    {
                        return new ValueTask<GameSessionHeartbeatResult>(
                            GameSessionHeartbeatResult.ConnectionOnly());
                    }

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
                if (state.PendingBinding is { } pending)
                {
                    if (pending.PreviousConnectionId is not null &&
                        pending.DisconnectedAt is null)
                    {
                        activeSessions++;
                        activeConnections++;
                    }
                    else if (pending.DisconnectedAt is not null)
                    {
                        disconnectedSessions++;
                    }

                    continue;
                }

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

    public ValueTask<IReadOnlyList<GameSessionExpiration>> ExpireSessionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expired = new List<GameSessionExpiration>();
        foreach (var item in _sessions)
        {
            var state = item.Value;
            GameSessionExpirationKind? kind;
            lock (state.Gate)
            {
                kind = GetExpirationKind(state, now);
                if (kind is null)
                    continue;
            }

            lock (_gate)
            {
                lock (state.Gate)
                {
                    kind = GetExpirationKind(state, now);
                    if (kind is null ||
                        !_sessions.TryRemove(item.Key, out _))
                    {
                        continue;
                    }

                    var connectionId = kind == GameSessionExpirationKind.Disconnected
                        ? state.LastDisconnectedConnectionId
                        : state.LastTerminatedConnectionId ?? state.LastDisconnectedConnectionId;
                    RemoveConnectionMapping(_connectionToSession, state.ConnectionId, state.Session);
                    RemoveConnectionMapping(
                        _connectionToSession,
                        state.LastDisconnectedConnectionId,
                        state.Session);
                    RemoveConnectionMapping(
                        _terminatedConnectionToSession,
                        state.LastTerminatedConnectionId,
                        state.Session);
                    state.ConnectionId = null;
                    state.PendingBinding = null;
                    state.Items.Clear();
                    state.ItemsSnapshot = GameSessionItems.Empty;
                    expired.Add(new GameSessionExpiration(state.Session, connectionId, kind.Value));
                }
            }
        }

        return new ValueTask<IReadOnlyList<GameSessionExpiration>>(expired);
    }

    private static GameSessionExpirationKind? GetExpirationKind(
        SessionState state,
        DateTimeOffset now)
    {
        if (state.Termination is not null)
        {
            return state.TerminalRetentionDeadlineUtc is { } terminalDeadline &&
                terminalDeadline <= now
                    ? GameSessionExpirationKind.RetainedTermination
                    : null;
        }

        return state.DisconnectedAt is not null &&
            state.ResumeDeadlineUtc is { } resumeDeadline &&
            resumeDeadline <= now
                ? GameSessionExpirationKind.Disconnected
                : null;
    }

    private void DisconnectState(SessionState state, string connectionId, DateTimeOffset disconnectedAt)
    {
        lock (state.Gate)
        {
            _connectionToSession.TryRemove(connectionId, out _);
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

    private GameSessionBindResult PrepareSessionBindingCore(
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

            if (state.PendingBinding is { } pending)
            {
                throw new InvalidOperationException(
                    $"Game session '{session}' already has a pending binding to RPC connection '{pending.ConnectionId}'.");
            }

            var previousConnectionId = state.ConnectionId;
            var sessionBecameActive = previousConnectionId is null;
            state.PendingBinding = new PendingBinding(
                connectionId,
                previousConnectionId,
                state.LastDisconnectedConnectionId,
                state.LastTerminatedConnectionId,
                state.DisconnectedAt,
                state.ResumeDeadlineUtc,
                state.ReliableReplayPending,
                state.LastHeartbeatAt);
            if (!string.Equals(previousConnectionId, connectionId, StringComparison.Ordinal))
            {
                if (previousConnectionId is not null)
                {
                    _connectionToSession.TryRemove(previousConnectionId, out _);
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

    private void CommitSessionBindingCore(GameSessionKey session, string connectionId)
    {
        if (!_sessions.TryGetValue(session, out var state))
        {
            throw new InvalidOperationException($"Game session '{session}' does not exist.");
        }

        lock (state.Gate)
        {
            if (state.PendingBinding is not { } pending ||
                !string.Equals(pending.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Game session '{session}' has no pending binding to RPC connection '{connectionId}'.");
            }

            if (!string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal) ||
                state.DisconnectedAt is not null ||
                state.Termination is not null)
            {
                throw new InvalidOperationException(
                    $"Game session '{session}' disconnected before its binding could be committed.");
            }

            state.PendingBinding = null;
        }
    }

    private static void RemoveConnectionMapping(
        ConcurrentDictionary<string, GameSessionKey> connections,
        string? connectionId,
        GameSessionKey session)
    {
        if (connectionId is not null &&
            connections.TryGetValue(connectionId, out var bound) &&
            bound == session)
        {
            connections.TryRemove(connectionId, out _);
        }
    }

    private static void ValidateSession(GameSessionKey session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.OwnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
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

        public PendingBinding? PendingBinding { get; set; }

        public SessionTerminationNotice? Termination { get; set; }

        public bool KeepTerminationForResume { get; set; }

        public DateTimeOffset? TerminalRetentionDeadlineUtc { get; set; }

        public Dictionary<string, GameSessionItemValue> Items { get; } = new(StringComparer.Ordinal);

        public GameSessionItems ItemsSnapshot = GameSessionItems.Empty;
    }

    private sealed record PendingBinding(
        string ConnectionId,
        string? PreviousConnectionId,
        string? LastDisconnectedConnectionId,
        string? LastTerminatedConnectionId,
        DateTimeOffset? DisconnectedAt,
        DateTimeOffset? ResumeDeadlineUtc,
        bool ReliableReplayPending,
        DateTimeOffset? LastHeartbeatAt);
}
