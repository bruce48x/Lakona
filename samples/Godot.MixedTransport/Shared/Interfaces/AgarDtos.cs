using System.Collections.Generic;
using MemoryPack;

namespace Shared.Interfaces;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LoginRequest
{
    [MemoryPackOrder(0)]
    public string Account { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Password { get; set; } = string.Empty;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LoginReply
{
    [MemoryPackOrder(0)]
    public int Code { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string Token { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public uint Conv { get; set; }

    [MemoryPackOrder(4)]
    public string KcpHost { get; set; } = string.Empty;

    [MemoryPackOrder(5)]
    public int KcpPort { get; set; }

    [MemoryPackOrder(6)]
    public string PlayerId { get; set; } = string.Empty;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class BattleJoinRequest
{
    [MemoryPackOrder(0)]
    public string Token { get; set; } = string.Empty;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class BattleJoinReply
{
    [MemoryPackOrder(0)]
    public int Code { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public string PlayerId { get; set; } = string.Empty;

    [MemoryPackOrder(3)]
    public float WorldWidth { get; set; }

    [MemoryPackOrder(4)]
    public float WorldHeight { get; set; }

    [MemoryPackOrder(5)]
    public float SpawnX { get; set; }

    [MemoryPackOrder(6)]
    public float SpawnY { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PlayerInputRequest
{
    [MemoryPackOrder(0)]
    public float DirectionX { get; set; }

    [MemoryPackOrder(1)]
    public float DirectionY { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class CommandReply
{
    [MemoryPackOrder(0)]
    public int Code { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = string.Empty;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class WorldSnapshotRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class WorldSnapshotReply
{
    [MemoryPackOrder(0)]
    public int Code { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public long Tick { get; set; }

    [MemoryPackOrder(3)]
    public string YourPlayerId { get; set; } = string.Empty;

    [MemoryPackOrder(4)]
    public float WorldWidth { get; set; }

    [MemoryPackOrder(5)]
    public float WorldHeight { get; set; }

    [MemoryPackOrder(6)]
    public List<PlayerBlobState> Players { get; set; } = new();

    [MemoryPackOrder(7)]
    public List<FoodState> Foods { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PlayerBlobState
{
    [MemoryPackOrder(0)]
    public string PlayerId { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public float X { get; set; }

    [MemoryPackOrder(3)]
    public float Y { get; set; }

    [MemoryPackOrder(4)]
    public float Radius { get; set; }

    [MemoryPackOrder(5)]
    public float Mass { get; set; }

    [MemoryPackOrder(6)]
    public bool IsSelf { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class FoodState
{
    [MemoryPackOrder(0)]
    public float X { get; set; }

    [MemoryPackOrder(1)]
    public float Y { get; set; }

    [MemoryPackOrder(2)]
    public float Radius { get; set; }
}
