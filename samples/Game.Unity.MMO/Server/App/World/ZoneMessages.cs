using Shared.Interfaces;
using Lakona.Game.Server.Sessions;
using MemoryPack;

namespace Game.Unity.MMO.Server.App.World;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ZoneEnterRequest
{
    [MemoryPackOrder(0)] public string CharacterId { get; set; } = "";
    [MemoryPackOrder(1)] public string CharacterName { get; set; } = "";
    [MemoryPackOrder(2)] public GameSessionKey Session { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ZoneEnterResult
{
    [MemoryPackOrder(0)] public bool Accepted { get; set; }
    [MemoryPackOrder(1)] public string Message { get; set; } = "";
    [MemoryPackOrder(2)] public WorldSnapshot Snapshot { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ZoneCommandRequest
{
    [MemoryPackOrder(0)] public string CharacterId { get; set; } = "";
    [MemoryPackOrder(1)] public GameSessionKey Session { get; set; }
    [MemoryPackOrder(2)] public CharacterCommand Command { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ZoneLeaveRequest
{
    [MemoryPackOrder(0)] public string CharacterId { get; set; } = "";
    [MemoryPackOrder(1)] public GameSessionKey Session { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ZoneTickRequest
{
    [MemoryPackOrder(0)] public DateTime ObservedAtUtc { get; set; }
}

public sealed class ZoneTimerArgs
{
    public string ZoneId { get; set; } = "";
}
