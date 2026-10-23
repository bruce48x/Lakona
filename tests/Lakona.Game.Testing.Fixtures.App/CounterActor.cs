using System.Runtime.CompilerServices;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using MemoryPack;

[assembly: InternalsVisibleTo("Lakona.Game.Testing.Fixtures.Hotfix")]

namespace Lakona.Game.Testing.Fixtures.App;

public readonly record struct CounterId(string Value);

[NodeRole("battle")]
public sealed class CounterActor : Actor<CounterId>
{
    internal int Value;
    internal CounterSnapshot? Snapshot;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class AddCounterRequest
{
    [MemoryPackOrder(0)]
    public int Delta { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class WaitCounterRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class CounterReply
{
    [MemoryPackOrder(0)]
    public int Value { get; set; }
    [MemoryPackOrder(1)] public CounterSnapshot? Snapshot { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class CounterStartArgs
{
    [MemoryPackOrder(0)] public int Value { get; set; }
    [MemoryPackOrder(1)] public bool Wait { get; set; }
    [MemoryPackOrder(2)] public bool Fail { get; set; }
    [MemoryPackOrder(3)] public CounterSnapshot? Snapshot { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class CounterSnapshot
{
    [MemoryPackOrder(0)] public string Name { get; set; } = "";
    [MemoryPackOrder(1)] public List<CounterItem> Items { get; set; } = [];
    [MemoryPackOrder(2)] public Dictionary<string, int> Scores { get; set; } = new();
    [MemoryPackOrder(3), CounterExternalIdFormatter] public CounterExternalId? ExternalId { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class CounterItem
{
    [MemoryPackOrder(0)] public int Id { get; set; }
    [MemoryPackOrder(1)] public string Label { get; set; } = "";
}
