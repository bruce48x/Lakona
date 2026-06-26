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
    private readonly Dictionary<string, PlayerSessionRegistration> _byPlayerId = new(StringComparer.Ordinal);

    public void RegisterControl(
        string playerId,
        string sessionToken,
        string connectionId,
        GameSessionKey session)
    {
        lock (_gate)
        {
            _byPlayerId[playerId] = new PlayerSessionRegistration(playerId, sessionToken)
            {
                ControlSessionKey = session,
                ConnectionId = connectionId
            };
        }
    }

    public bool UpdateControlConnection(
        string playerId,
        string sessionToken,
        string connectionId,
        GameSessionKey session)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                return false;
            }

            if (!string.Equals(registration.SessionToken, sessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            registration.ControlSessionKey = session;
            registration.ConnectionId = connectionId;
            registration.SessionToken = sessionToken;
            return true;
        }
    }

    public void DisconnectControl(string playerId, string? connectionId)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionId) &&
                !string.Equals(registration.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return;
            }

            registration.ConnectionId = string.Empty;
        }
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

    public bool AttachRealtime(
        string playerId,
        string sessionToken,
        string roomId,
        string matchId,
        string connectionId,
        GameSessionKey session)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration = new PlayerSessionRegistration(playerId, sessionToken);
                _byPlayerId[playerId] = registration;
            }

            if (!string.Equals(registration.SessionToken, sessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            registration.RoomId = roomId;
            registration.MatchId = matchId;
            registration.RealtimeSessionKey = session;
            registration.RealtimeConnectionId = connectionId;
            return true;
        }
    }

    public void DetachRealtime(string playerId, string? connectionId = null)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionId) &&
                !string.Equals(registration.RealtimeConnectionId, connectionId, StringComparison.Ordinal))
            {
                return;
            }

            registration.RealtimeConnectionId = null;
            registration.RealtimeSessionKey = null;
            if (registration.ControlSessionKey is null)
            {
                _byPlayerId.Remove(playerId);
            }
        }
    }

    public void ClearRoom(string playerId, string? expectedRoomId = null)
    {
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
            registration.RealtimeConnectionId = null;
            registration.RealtimeSessionKey = null;
            if (registration.ControlSessionKey is null)
            {
                _byPlayerId.Remove(playerId);
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
                registration.ControlSessionKey is { } session &&
                string.Equals(ReliablePushSessionOwnerKey.Create(session), ownerKey, StringComparison.Ordinal));
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
