using Shared.Interfaces;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;

namespace Gateway.Services;

internal sealed class SessionDirectory
{
    private readonly Lock _gate = new();
    private readonly IGameSessionDirectory _gameSessions;
    private readonly Dictionary<string, SessionRegistration> _byPlayerId = new(StringComparer.Ordinal);

    public SessionDirectory()
        : this(new InMemoryGameSessionDirectory())
    {
    }

    public SessionDirectory(IGameSessionDirectory gameSessions)
    {
        _gameSessions = gameSessions;
    }

    public void Register(string playerId, string sessionToken, string connectionId, IPlayerCallback callback, bool preserveSessionState)
    {
        RegisterAsync(playerId, sessionToken, connectionId, callback, preserveSessionState)
            .GetAwaiter()
            .GetResult();
    }

    public async ValueTask<GameSessionKey> RegisterNewControlAsync(
        string playerId,
        string sessionToken,
        string connectionId,
        IPlayerCallback callback,
        CancellationToken cancellationToken = default)
    {
        var session = await _gameSessions.StartNewSessionAsync(playerId, cancellationToken).ConfigureAwait(false);
        await BindControlAsync(session, connectionId, callback, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _byPlayerId[playerId] = new SessionRegistration(session, sessionToken, connectionId, callback);
            return session;
        }
    }

    public async ValueTask<SessionResumeDecision> ResumeControlAsync(
        string playerId,
        string sessionToken,
        string connectionId,
        IPlayerCallback callback,
        CancellationToken cancellationToken = default)
    {
        SessionRegistration? registration;
        lock (_gate)
        {
            _byPlayerId.TryGetValue(playerId, out registration);
        }

        if (registration is null)
        {
            return SessionResumeDecision.StateLost("Session was not found in the gateway directory.");
        }

        if (!string.Equals(registration.SessionToken, sessionToken, StringComparison.Ordinal))
        {
            return SessionResumeDecision.StateLost("Session token changed before reconnect.");
        }

        var decision = await _gameSessions.TryResumeAsync(registration.SessionKey, cancellationToken)
            .ConfigureAwait(false);
        if (decision.Status != SessionResumeStatus.Resumed || decision.Session is null)
        {
            return decision;
        }

        await BindControlAsync(decision.Session.Value, connectionId, callback, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            registration.SessionKey = decision.Session.Value;
            registration.SessionToken = sessionToken;
            registration.ConnectionId = connectionId;
            registration.ControlCallback = callback;
            registration.ControlDisconnectedAtUtc = null;
        }

        return decision;
    }

    public async ValueTask<IPlayerCallback?> GetControlCallbackAsync(SessionRegistration registration, CancellationToken cancellationToken = default)
    {
        return await _gameSessions.GetCallbackAsync<IPlayerCallback>(
            new SessionEndpointKey(registration.SessionKey, SessionRegistration.ControlEndpointName),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IPlayerCallback?> GetRealtimePreferredCallbackAsync(SessionRegistration registration, CancellationToken cancellationToken = default)
    {
        var realtime = await _gameSessions.GetCallbackAsync<IPlayerCallback>(
            new SessionEndpointKey(registration.SessionKey, SessionRegistration.RealtimeEndpointName),
            cancellationToken).ConfigureAwait(false);

        return realtime ?? await GetControlCallbackAsync(registration, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RegisterAsync(string playerId, string sessionToken, string connectionId, IPlayerCallback callback, bool preserveSessionState)
    {
        SessionRegistration? existing;
        lock (_gate)
        {
            _byPlayerId.TryGetValue(playerId, out existing);
        }

        if (existing is null)
        {
            var session = await _gameSessions.StartNewSessionAsync(playerId).ConfigureAwait(false);
            await BindControlAsync(session, connectionId, callback).ConfigureAwait(false);

            lock (_gate)
            {
                _byPlayerId[playerId] = new SessionRegistration(session, sessionToken, connectionId, callback);
            }

            return;
        }

        var sessionKey = preserveSessionState
            ? existing.SessionKey
            : await _gameSessions.StartNewSessionAsync(playerId).ConfigureAwait(false);
        await BindControlAsync(sessionKey, connectionId, callback).ConfigureAwait(false);

        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                _byPlayerId[playerId] = new SessionRegistration(sessionKey, sessionToken, connectionId, callback);
                return;
            }

            registration.SessionKey = sessionKey;
            registration.SessionToken = sessionToken;
            registration.ConnectionId = connectionId;
            registration.ControlCallback = callback;
            registration.ControlDisconnectedAtUtc = null;
            if (!preserveSessionState)
            {
                registration.RealtimeConnectionId = null;
                registration.RealtimeCallback = null;
                registration.RoomId = null;
                registration.MatchId = null;
                registration.SeatIndex = -1;
                registration.MatchmakingTicketId = null;
            }
        }
    }

    public async ValueTask MarkControlDisconnectedAsync(string playerId, string? connectionId, DateTime disconnectedAtUtc, CancellationToken cancellationToken = default)
    {
        SessionRegistration? registration;
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionId) &&
                !string.Equals(registration.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return;
            }

            registration.ConnectionId = string.Empty;
            registration.ControlCallback = null;
            registration.ControlDisconnectedAtUtc = disconnectedAtUtc;
        }

        await _gameSessions.MarkEndpointDisconnectedAsync(
            new SessionEndpointKey(registration.SessionKey, SessionRegistration.ControlEndpointName),
            connectionId,
            cancellationToken).ConfigureAwait(false);
    }

    public void SetQueueTicket(string playerId, string? ticketId)
    {
        lock (_gate)
        {
            if (_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration.MatchmakingTicketId = string.IsNullOrWhiteSpace(ticketId) ? null : ticketId;
            }
        }
    }

    public void AssignRoom(string playerId, string roomId, string matchId, int seatIndex)
    {
        lock (_gate)
        {
            if (_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration.RoomId = roomId;
                registration.MatchId = matchId;
                registration.SeatIndex = seatIndex;
                registration.MatchmakingTicketId = null;
            }
        }
    }

    public bool AttachRealtime(string playerId, string sessionToken, string roomId, string matchId, string connectionId, IPlayerCallback callback)
    {
        return AttachRealtimeAsync(playerId, sessionToken, roomId, matchId, connectionId, callback)
            .GetAwaiter()
            .GetResult();
    }

    public async ValueTask<bool> AttachRealtimeAsync(
        string playerId,
        string sessionToken,
        string roomId,
        string matchId,
        string connectionId,
        IPlayerCallback callback,
        CancellationToken cancellationToken = default)
    {
        GameSessionKey session;
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                session = _gameSessions.StartNewSessionAsync(playerId, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                registration = new SessionRegistration(session, sessionToken, string.Empty, controlCallback: null)
                {
                    RoomId = roomId,
                    MatchId = matchId
                };
                _byPlayerId[playerId] = registration;
            }
            else
            {
                session = registration.SessionKey;
            }

            if (!string.Equals(registration.SessionToken, sessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            registration.RoomId = roomId;
            registration.MatchId = matchId;
            registration.RealtimeConnectionId = connectionId;
            registration.RealtimeCallback = callback;
        }

        await _gameSessions.BindEndpointAsync(
            new SessionEndpointKey(session, SessionRegistration.RealtimeEndpointName),
            connectionId,
            callback,
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async ValueTask DetachRealtimeAsync(string playerId, string? connectionId = null, CancellationToken cancellationToken = default)
    {
        SessionRegistration? registration;
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionId) &&
                !string.Equals(registration.RealtimeConnectionId, connectionId, StringComparison.Ordinal))
            {
                return;
            }

            registration.RealtimeConnectionId = null;
            registration.RealtimeCallback = null;
            if (registration.ControlCallback is null)
            {
                _byPlayerId.Remove(playerId);
            }
        }

        await _gameSessions.MarkEndpointDisconnectedAsync(
            new SessionEndpointKey(registration.SessionKey, SessionRegistration.RealtimeEndpointName),
            connectionId,
            cancellationToken).ConfigureAwait(false);
    }

    public void DetachRealtime(string playerId, string? connectionId = null)
    {
        DetachRealtimeAsync(playerId, connectionId).GetAwaiter().GetResult();
    }

    public void ClearRoom(string playerId, string? expectedRoomId = null)
    {
        SessionRegistration? detachedRegistration = null;
        string? realtimeConnectionId = null;
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(expectedRoomId) &&
                !string.Equals(registration.RoomId, expectedRoomId, StringComparison.Ordinal))
            {
                return;
            }

            registration.RoomId = null;
            registration.MatchId = null;
            registration.SeatIndex = -1;
            detachedRegistration = registration;
            realtimeConnectionId = registration.RealtimeConnectionId;
            registration.RealtimeConnectionId = null;
            registration.RealtimeCallback = null;
            if (registration.ControlCallback is null)
            {
                _byPlayerId.Remove(playerId);
            }
        }

        if (detachedRegistration is not null)
        {
            _gameSessions.MarkEndpointDisconnectedAsync(
                new SessionEndpointKey(detachedRegistration.SessionKey, SessionRegistration.RealtimeEndpointName),
                realtimeConnectionId)
                .GetAwaiter()
                .GetResult();
        }
    }

    public SessionRegistration? Get(string playerId)
    {
        lock (_gate)
        {
            return _byPlayerId.TryGetValue(playerId, out var registration)
                ? registration
                : null;
        }
    }

    public SessionRegistration? GetByReliablePushOwnerKey(string ownerKey)
    {
        lock (_gate)
        {
            return _byPlayerId.Values.FirstOrDefault(registration =>
                string.Equals(ReliablePushSessionOwnerKey.Create(registration.SessionKey), ownerKey, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<SessionRegistration> GetMany(IEnumerable<string> playerIds)
    {
        lock (_gate)
        {
            return playerIds
                .Select(playerId => _byPlayerId.TryGetValue(playerId, out var registration) ? registration : null)
                .Where(static registration => registration is not null)
                .Cast<SessionRegistration>()
                .ToArray();
        }
    }

    public IReadOnlyList<SessionRegistration> GetByRoom(string roomId)
    {
        lock (_gate)
        {
            return _byPlayerId.Values
                .Where(static registration => !string.IsNullOrWhiteSpace(registration.RoomId))
                .Where(registration => string.Equals(registration.RoomId, roomId, StringComparison.Ordinal))
                .ToArray();
        }
    }

    public IReadOnlyList<SessionRegistration> GetExpiredControlDisconnects(DateTime nowUtc, TimeSpan gracePeriod)
    {
        lock (_gate)
        {
            return _byPlayerId.Values
                .Where(registration => registration.ControlCallback is null)
                .Where(registration => registration.ControlDisconnectedAtUtc is DateTime disconnectedAtUtc &&
                                       nowUtc - disconnectedAtUtc >= gracePeriod)
                .ToArray();
        }
    }

    public void Remove(string playerId)
    {
        lock (_gate)
        {
            _byPlayerId.Remove(playerId);
        }
    }

    private async ValueTask BindControlAsync(
        GameSessionKey session,
        string connectionId,
        IPlayerCallback callback,
        CancellationToken cancellationToken = default)
    {
        await _gameSessions.BindEndpointAsync(
            new SessionEndpointKey(session, SessionRegistration.ControlEndpointName),
            connectionId,
            callback,
            cancellationToken).ConfigureAwait(false);
    }
}
