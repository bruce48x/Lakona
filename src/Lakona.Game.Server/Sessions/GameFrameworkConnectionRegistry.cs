using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameFrameworkConnectionRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, RpcSession> sessions = new(StringComparer.Ordinal);

    public void Set(RpcSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            sessions[session.ContextId] = session;
        }
    }

    public RpcSession? Get(string connectionId)
    {
        lock (gate)
        {
            return sessions.TryGetValue(connectionId, out var session) ? session : null;
        }
    }

    public void Remove(string connectionId)
    {
        lock (gate)
        {
            sessions.Remove(connectionId);
        }
    }
}
