using Lakona.Game.Server.Hotfix.Abstractions;
using MemoryPack;

namespace Server.Hotfix.Services;

internal static class StateStoreUserActorPlacement
{
    public const string FeatureName = "state-store";

    public const int CreateUserActorCommandId = 201;
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(StateStoreUserActorPlacement.CreateUserActorCommandId)]
public partial class CreateUserActorRequest
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class CreateActorReply
{
    [MemoryPackOrder(0)]
    public bool Succeeded { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = "";
}
