using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

public static class ClientNotificationRouteKey
{
    public static RouteKey FromSession(GameSessionKey session)
    {
        return new RouteKey($"client-session:{session.OwnerKey}/{session.SessionId}");
    }
}
