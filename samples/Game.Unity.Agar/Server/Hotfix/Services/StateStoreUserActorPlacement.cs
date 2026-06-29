using Lakona.Game.Server.Hotfix.Abstractions;
using MemoryPack;

namespace Server.Hotfix.Services;

internal static class StateStoreUserActorPlacement
{
    public const string FeatureName = "state-store";

    public const int EnsureUserActorCommandId = 201;

    public const int EnsureLeaderboardActorCommandId = 202;
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(StateStoreUserActorPlacement.EnsureUserActorCommandId)]
public partial class EnsureUserActorRequest
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(StateStoreUserActorPlacement.EnsureLeaderboardActorCommandId)]
public partial class EnsureLeaderboardActorRequest
{
    [MemoryPackOrder(0)]
    public string LeaderboardId { get; set; } = "";
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class EnsureActorReply
{
    [MemoryPackOrder(0)]
    public bool Succeeded { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = "";
}
