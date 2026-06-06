using System.Globalization;
using Shared.Interfaces;
using Lakona.Rpc.Server;

namespace Agar.MixedTransport.Server.Services;

public sealed class BattleService : IBattleService
{
    private readonly RpcSession _session;
    private readonly IBattleNotifications _notifications;
    private readonly LoginTicketStore _loginTickets;
    private readonly BattleWorld _world;
    private LoginGrant? _grant;

    public BattleService(RpcSession session, IBattleNotifications notifications, LoginTicketStore loginTickets, BattleWorld world)
    {
        _session = session;
        _notifications = notifications;
        _loginTickets = loginTickets;
        _world = world;
        _session.Disconnected += _ => Unsubscribe();
    }

    public ValueTask<BattleJoinReply> JoinAsync(BattleJoinRequest request)
    {
        var conv = ParseConversationId(_session.ContextId);
        if (!_loginTickets.TryClaimBattle(request.Token, conv, _session.RemoteAddress, out var grant))
        {
            return ValueTask.FromResult(new BattleJoinReply
            {
                Code = 401,
                Message = "Battle join rejected. Login again to obtain a fresh KCP ticket."
            });
        }

        _grant = grant;
        var joined = _world.JoinOrRespawn(grant.PlayerId, grant.Account);
        _world.RegisterSubscriber(grant.PlayerId, _notifications);
        return ValueTask.FromResult(new BattleJoinReply
        {
            Code = 0,
            Message = "Battle join ok.",
            PlayerId = joined.PlayerId,
            WorldWidth = joined.WorldWidth,
            WorldHeight = joined.WorldHeight,
            SpawnX = joined.SpawnX,
            SpawnY = joined.SpawnY
        });
    }

    public ValueTask<CommandReply> UpdateInputAsync(PlayerInputRequest request)
    {
        if (_grant is null)
        {
            return ValueTask.FromResult(new CommandReply
            {
                Code = 401,
                Message = "Join the battle before sending movement input."
            });
        }

        _world.UpdateInput(_grant.PlayerId, request.DirectionX, request.DirectionY);
        return ValueTask.FromResult(new CommandReply
        {
            Code = 0,
            Message = "ok"
        });
    }

    private void Unsubscribe()
    {
        if (_grant is not null)
            _world.UnregisterSubscriber(_grant.PlayerId);
    }

    private static uint ParseConversationId(string contextId)
    {
        const string marker = "conv=";
        var markerIndex = contextId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            throw new InvalidOperationException($"KCP session context does not contain a conversation id: {contextId}");

        markerIndex += marker.Length;
        var valueEnd = contextId.IndexOf(' ', markerIndex);
        var convText = valueEnd >= 0
            ? contextId[markerIndex..valueEnd]
            : contextId[markerIndex..];

        if (!uint.TryParse(convText, NumberStyles.None, CultureInfo.InvariantCulture, out var conv) || conv == 0)
            throw new InvalidOperationException($"Unable to parse KCP conversation id from context: {contextId}");

        return conv;
    }
}
