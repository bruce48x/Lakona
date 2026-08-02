using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Shared.Interfaces;

namespace Game.Unity.MMO.Server.Hotfix.World;

[HotfixComponent]
public sealed class ZoneNotifier
{
    private readonly IClientNotifications _notifications;
    public ZoneNotifier(IClientNotifications notifications) => _notifications = notifications;

    public void Snapshot(GameSessionKey recipient, WorldSnapshot snapshot)
    {
        _notifications.ForSession<IWorldCallback>(recipient).OnWorldSnapshot(snapshot);
    }
}
