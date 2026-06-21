using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hotfix;

public interface IGameSessionLifecycle
{
    [RpcMethod(GameSessionLifecycleMethodIds.SessionExpired)]
    ValueTask SessionExpiredAsync(GameSessionExpiredRequest request);
}

public static class GameSessionLifecycleMethodIds
{
    public const int SessionExpired = 1;
}

public sealed class GameSessionExpiredRequest
{
    public string OwnerKey { get; set; } = "";

    public string SessionId { get; set; } = "";

    public long Generation { get; set; }

    public string ConnectionId { get; set; } = "";

    public List<string> CallbackContractTypeNames { get; set; } = [];
}
