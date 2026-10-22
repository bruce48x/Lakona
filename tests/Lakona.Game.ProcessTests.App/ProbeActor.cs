using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using MemoryPack;

namespace Lakona.Game.ProcessTests.App;

public readonly record struct ProbeId(string Value);

[NodeRole("worker")]
public sealed class ProbeActor : Actor<ProbeId>
{
    internal int Count;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ProbeRequest
{
    [MemoryPackOrder(0)] public string RequestId { get; set; } = "";
    [MemoryPackOrder(1)] public bool Hold { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ProbeReply
{
    [MemoryPackOrder(0)] public string Node { get; set; } = "";
    [MemoryPackOrder(1)] public string Incarnation { get; set; } = "";
    [MemoryPackOrder(2)] public string Activation { get; set; } = "";
    [MemoryPackOrder(3)] public string RequestId { get; set; } = "";
    [MemoryPackOrder(4)] public int Count { get; set; }
}
