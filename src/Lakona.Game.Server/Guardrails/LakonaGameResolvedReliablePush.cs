namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedReliablePush(
    LakonaGameResolvedValue<string> StorageMode,
    LakonaGameResolvedValue<int> PendingLimit,
    LakonaGameResolvedValue<int> ReplayWindowSeconds,
    bool HasSessionIdentityResolver);
