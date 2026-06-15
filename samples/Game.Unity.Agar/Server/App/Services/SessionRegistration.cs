using Shared.Interfaces;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Server.App.Services;

internal sealed class SessionRegistration
{
    public SessionRegistration(GameSessionKey sessionKey, string sessionToken, string connectionId, IPlayerCallback? controlCallback)
    {
        ControlSessionKey = sessionKey;
        PlayerId = sessionKey.OwnerKey;
        SessionToken = sessionToken;
        ConnectionId = connectionId;
        ControlCallback = controlCallback;
    }

    public GameSessionKey SessionKey
    {
        get => ControlSessionKey;
        set => ControlSessionKey = value;
    }

    public GameSessionKey ControlSessionKey { get; set; }
    public GameSessionKey? RealtimeSessionKey { get; set; }
    public string PlayerId { get; }
    public string SessionToken { get; set; }
    public string ConnectionId { get; set; }
    public IPlayerCallback? ControlCallback { get; set; }
    public IPlayerCallback? RealtimeCallback { get; set; }
    public string? RealtimeConnectionId { get; set; }
    public DateTime? ControlDisconnectedAtUtc { get; set; }
    public string? RoomId { get; set; }
    public string? MatchId { get; set; }
    public int SeatIndex { get; set; } = -1;
    public string? MatchmakingTicketId { get; set; }

    public IPlayerCallback? GetRealtimePreferredCallback()
    {
        return RealtimeCallback ?? ControlCallback;
    }
}
