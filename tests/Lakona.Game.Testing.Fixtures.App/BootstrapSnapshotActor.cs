using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using MemoryPack;

namespace Lakona.Game.Testing.Fixtures.App;

public sealed class InvalidBootstrapStartArgs
{
    public BootstrapSnapshot Snapshot { get; set; } = new();
    public bool Fail { get; set; }
}

[NodeRole("probe")]
public sealed class BootstrapSnapshotActor : Actor<string>
{
    public BootstrapSnapshot? Snapshot;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class BootstrapStartArgs
{
    [MemoryPackOrder(0)] public BootstrapSnapshot Snapshot { get; set; } = new();
    [MemoryPackOrder(1)] public bool Fail { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class BootstrapSnapshot
{
    [MemoryPackOrder(0)] public string Name { get; set; } = "";
    [MemoryPackOrder(1)] public List<int> Items { get; set; } = [];
    [MemoryPackOrder(2)] public Dictionary<string, int> Scores { get; set; } = new();
}
