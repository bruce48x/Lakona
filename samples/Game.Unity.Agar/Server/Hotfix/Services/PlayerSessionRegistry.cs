using Shared.Interfaces;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;

namespace Server.Hotfix.Services;

internal enum PlayerConnectionKind
{
    Control,
    Realtime
}

internal sealed record PlayerConnectionRegistration(
    string PlayerId,
    string ConnectionId,
    PlayerConnectionKind Kind);

internal sealed class PlayerSessionRegistry
{
    private readonly Lock _gate = new();
    private readonly IGameSessionRegistry _gameSessions;
    private readonly Dictionary<string, PlayerSessionRegistration> _byPlayerId = new(StringComparer.Ordinal);

    public PlayerSessionRegistry()
        : this(new InMemoryGameSessionRegistry())
    {
    }

    public PlayerSessionRegistry(IGameSessionRegistry gameSessions)
    {
        _gameSessions = gameSessions;
    }

    public async ValueTask<GameSessionKey> RegisterNewControlAsync(
        string playerId,
        string sessionToken,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _gameSessions.StartNewSessionAsync(playerId, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _byPlayerId[playerId] = new PlayerSessionRegistration(session, sessionToken, connectionId);
            return session;
        }
    }

    public async ValueTask<SessionResumeDecision> ResumeControlAsync(
        string playerId,
        string sessionToken,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        PlayerSessionRegistration? registration;
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

        lock (_gate)
        {
            registration.SessionKey = decision.Session.Value;
            registration.SessionToken = sessionToken;
            registration.ConnectionId = connectionId;
            registration.ControlCallback = null;
        }

        return decision;
    }

    public async ValueTask<bool> BindControlCallbackAsync(
        string playerId,
        string connectionId,
        IControlCallback callback,
        CancellationToken cancellationToken = default)
    {
        PlayerSessionRegistration? registration;
        bool shouldBind;
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out registration))
            {
                return false;
            }

            if (!string.Equals(registration.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return false;
            }

            if (registration.ControlCallback is not null)
            {
                return false;
            }

            registration.ControlCallback = callback;
            shouldBind = true;
        }

        if (!shouldBind)
        {
            return false;
        }

        await _gameSessions.BindSessionAsync(
            registration.ControlSessionKey,
            connectionId,
            callback,
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async ValueTask<IControlCallback?> GetControlCallbackAsync(PlayerSessionRegistration registration, CancellationToken cancellationToken = default)
    {
        return await _gameSessions.GetCallbackAsync<IControlCallback>(
            registration.ControlSessionKey,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IBattleCallback?> GetRealtimeCallbackAsync(PlayerSessionRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration.RealtimeSessionKey is not { } realtimeSession)
        {
            return null;
        }

        return await _gameSessions.GetCallbackAsync<IBattleCallback>(
            realtimeSession,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisconnectControlAsync(string playerId, string? connectionId, CancellationToken cancellationToken = default)
    {
        PlayerSessionRegistration? registration;
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
        }

        await _gameSessions.MarkSessionDisconnectedAsync(
            registration.ControlSessionKey,
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

    public bool AttachRealtime(string playerId, string sessionToken, string roomId, string matchId, string connectionId, IBattleCallback callback)
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
        IBattleCallback callback,
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
                registration = new PlayerSessionRegistration(session, sessionToken, string.Empty)
                {
                    RoomId = roomId,
                    MatchId = matchId
                };
                _byPlayerId[playerId] = registration;
            }
            else
            {
                session = registration.RealtimeSessionKey ?? _gameSessions.StartNewSessionAsync(playerId, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }

            if (!string.Equals(registration.SessionToken, sessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            registration.RoomId = roomId;
            registration.MatchId = matchId;
            registration.RealtimeSessionKey = session;
            registration.RealtimeConnectionId = connectionId;
            registration.RealtimeCallback = callback;
        }

        await _gameSessions.BindSessionAsync(
            session,
            connectionId,
            callback,
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async ValueTask DetachRealtimeAsync(string playerId, string? connectionId = null, CancellationToken cancellationToken = default)
    {
        PlayerSessionRegistration? registration;
        GameSessionKey? realtimeSession = null;
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
            realtimeSession = registration.RealtimeSessionKey;
            registration.RealtimeSessionKey = null;
            if (registration.ControlCallback is null)
            {
                _byPlayerId.Remove(playerId);
            }
        }

        if (realtimeSession is { } session)
        {
            await _gameSessions.MarkSessionDisconnectedAsync(
                session,
                connectionId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public void DetachRealtime(string playerId, string? connectionId = null)
    {
        DetachRealtimeAsync(playerId, connectionId).GetAwaiter().GetResult();
    }

    public void ClearRoom(string playerId, string? expectedRoomId = null)
    {
        PlayerSessionRegistration? detachedRegistration = null;
        string? realtimeConnectionId = null;
        GameSessionKey? realtimeSession = null;
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
            realtimeSession = registration.RealtimeSessionKey;
            registration.RealtimeConnectionId = null;
            registration.RealtimeCallback = null;
            registration.RealtimeSessionKey = null;
            if (registration.ControlCallback is null)
            {
                _byPlayerId.Remove(playerId);
            }
        }

        if (detachedRegistration is not null)
        {
            if (realtimeSession is { } session)
            {
                _gameSessions.MarkSessionDisconnectedAsync(
                    session,
                    realtimeConnectionId)
                    .GetAwaiter()
                    .GetResult();
            }
        }
    }

    public PlayerSessionRegistration? Get(string playerId)
    {
        lock (_gate)
        {
            return _byPlayerId.TryGetValue(playerId, out var registration)
                ? registration
                : null;
        }
    }

    public string? GetPlayerIdByConnection(string connectionId)
    {
        return GetConnection(connectionId)?.PlayerId;
    }

    public PlayerConnectionRegistration? GetConnection(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return null;
        }

        lock (_gate)
        {
            foreach (var registration in _byPlayerId.Values)
            {
                if (string.Equals(registration.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    return new PlayerConnectionRegistration(
                        registration.PlayerId,
                        connectionId,
                        PlayerConnectionKind.Control);
                }

                if (string.Equals(registration.RealtimeConnectionId, connectionId, StringComparison.Ordinal))
                {
                    return new PlayerConnectionRegistration(
                        registration.PlayerId,
                        connectionId,
                        PlayerConnectionKind.Realtime);
                }
            }
        }

        return null;
    }

    public PlayerSessionRegistration? GetByReliablePushOwnerKey(string ownerKey)
    {
        lock (_gate)
        {
            return _byPlayerId.Values.FirstOrDefault(registration =>
                string.Equals(ReliablePushSessionOwnerKey.Create(registration.SessionKey), ownerKey, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<PlayerSessionRegistration> GetMany(IEnumerable<string> playerIds)
    {
        lock (_gate)
        {
            return playerIds
                .Select(playerId => _byPlayerId.TryGetValue(playerId, out var registration) ? registration : null)
                .Where(static registration => registration is not null)
                .Cast<PlayerSessionRegistration>()
                .ToArray();
        }
    }

    public IReadOnlyList<PlayerSessionRegistration> GetByRoom(string roomId)
    {
        lock (_gate)
        {
            return _byPlayerId.Values
                .Where(static registration => !string.IsNullOrWhiteSpace(registration.RoomId))
                .Where(registration => string.Equals(registration.RoomId, roomId, StringComparison.Ordinal))
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

}
