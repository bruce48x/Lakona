using Shared.Interfaces;
using Lakona.Rpc.Server;

namespace Agar.MixedTransport.Server.Services;

public sealed class BattleService : IBattleService, IAsyncDisposable
{
    private readonly RpcConnectionInfo _connection;
    private readonly IBattleNotifications _notifications;
    private readonly LoginTicketStore _loginTickets;
    private readonly BattleWorld _world;
    private LoginGrant? _grant;

    public BattleService(RpcConnectionInfo connection, IBattleNotifications notifications, LoginTicketStore loginTickets, BattleWorld world)
    {
        _connection = connection;
        _notifications = notifications;
        _loginTickets = loginTickets;
        _world = world;
    }

    public ValueTask<BattleJoinReply> JoinAsync(BattleJoinRequest request)
    {
        if (!_loginTickets.TryClaimBattle(request.Token, _connection.RemoteEndPoint, out var grant))
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

    public ValueTask DisposeAsync()
    {
        Unsubscribe();
        return default;
    }
}
