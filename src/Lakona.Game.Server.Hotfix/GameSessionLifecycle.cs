using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hotfix;

public interface IGameSessionLifecycle
{
    [RpcMethod(GameSessionLifecycleMethodIds.SessionDisconnected)]
    ValueTask SessionDisconnectedAsync(GameSessionDisconnectedRequest request);

    [RpcMethod(GameSessionLifecycleMethodIds.SessionExpired)]
    ValueTask SessionExpiredAsync(GameSessionExpiredRequest request);
}

public static class GameSessionLifecycleMethodIds
{
    public const int SessionExpired = 1;
    public const int SessionDisconnected = 2;
}

public sealed class GameSessionDisconnectedRequest
{
    public string OwnerKey { get; set; } = "";

    public string SessionId { get; set; } = "";

    public long Generation { get; set; }

    public string ConnectionId { get; set; } = "";

    public List<string> CallbackContractTypeNames { get; set; } = [];
}

public sealed class GameSessionExpiredRequest
{
    public string OwnerKey { get; set; } = "";

    public string SessionId { get; set; } = "";

    public long Generation { get; set; }

    public string ConnectionId { get; set; } = "";

    public List<string> CallbackContractTypeNames { get; set; } = [];
}
